//! Operator-owned prices, used before a capped operation can reach the node.
use crate::error::{ApiError, ApiResult};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value, json};

/// A price for a specific operation/provider. Unknown prices are never zero.
#[derive(Debug, Clone, PartialEq, Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
pub struct OpPrice {
    /// Binding provenance: published or operator. Missing provenance is unpriced.
    #[serde(default)]
    pub source: String,
    /// Provider list-price URL for published tariffs.
    #[serde(default)]
    pub url: String,
    /// Explicit operator declaration for estimates.
    #[serde(default)]
    pub note: String,
    /// Provider model pinned by the installed node configuration (not a caller override).
    #[serde(default)]
    pub model: String,
    /// Studio operation: image, tts, 3d or describe.
    pub op: String,
    /// Provider pinned into the downstream request.
    pub provider: String,
    /// Published USD per image, character or call.
    pub per_unit: f64,
    /// image, character, bailian_character or call (only use call for a provider with a fixed call tariff).
    pub unit: String,
}

impl OpPrice {
    /// Only declared, positive tariffs can bind a ceiling.
    pub fn verified(&self) -> bool {
        self.per_unit.is_finite()
            && self.per_unit > 0.0
            && match self.source.as_str() {
                "published" => self.url.starts_with("https://") && self.url.len() > 8,
                "operator" => {
                    !self.note.trim().is_empty() && !self.note.contains("SET_BY_OPERATOR")
                }
                _ => false,
            }
    }
    /// Public provenance for diagnostics and ledger entries.
    pub fn tariff(&self) -> Value {
        json!({"kind":self.source,"provider":self.provider,"model":self.model,
            "unit":self.unit,"perUnitUsd":self.per_unit,"url":self.url,"note":self.note})
    }
}

/// A binding preflight charge, retained with the operation's idempotency key.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Charge {
    /// Tariff provenance, including kind.
    pub tariff: Value,
    /// Billable units in the bounded request.
    pub quantity: f64,
    /// USD charged under this binding tariff (operator values are estimates).
    pub cost_usd: f64,
}

/// Check an estimate and pin its provider so a caller cannot select an unpriced fallback.
pub fn check(
    prices: &[OpPrice],
    op: &str,
    input: &mut Map<String, Value>,
    max: f64,
) -> ApiResult<Charge> {
    let requested = input.get("provider").and_then(Value::as_str);
    let price = prices.iter().find(|p| p.op == op && requested.is_none_or(|r| r == p.provider)
        && p.verified()).ok_or_else(||
        ApiError::new(axum::http::StatusCode::CONFLICT, "budget_unpriced", format!("no verified price for {op} and the selected provider"))
            .with_hint("configure ops_prices in the companion config.toml and the matching node provider tariff"))?;
    // Parameters or input references can change a provider's tariff. Such variants need a
    // separate priced provider; never assume the default price covers them.
    if input.contains_key("model")
        || input
            .get("params")
            .is_some_and(|v| v.as_object().is_none_or(|v| !v.is_empty()))
        || input
            .get("references")
            .is_some_and(|v| v.as_array().is_none_or(|v| !v.is_empty()))
    {
        return Err(ApiError::new(
            axum::http::StatusCode::CONFLICT,
            "budget_unpriced",
            "no price for model, parameter or reference overrides",
        ));
    }
    let quantity = match (op, price.unit.as_str()) {
        ("image", "image") => match input.get("count") {
            None => 1.0,
            Some(v) => v
                .as_u64()
                .filter(|n| *n > 0)
                .ok_or_else(|| ApiError::bad_request("count must be positive"))?
                as f64,
        },
        ("tts", "character") => input
            .get("text")
            .and_then(Value::as_str)
            .ok_or_else(|| ApiError::bad_request("priced TTS requires inline text"))?
            .chars()
            .count() as f64,
        ("tts", "bailian_character") => bailian_characters(
            input
                .get("text")
                .and_then(Value::as_str)
                .ok_or_else(|| ApiError::bad_request("priced TTS requires inline text"))?,
        ) as f64,
        ("describe" | "3d", "call") => 1.0,
        _ => {
            return Err(ApiError::new(
                axum::http::StatusCode::CONFLICT,
                "budget_unpriced",
                "price unit does not match the operation",
            ));
        }
    };
    let estimate = quantity * price.per_unit;
    if !estimate.is_finite() || estimate > max {
        return Err(ApiError::new(
            axum::http::StatusCode::CONFLICT,
            "over_budget",
            format!("{op} estimate ${estimate} exceeds max_cost_usd ${max}"),
        )
        .with_data(json!({"estimatedCostUsd":estimate,"maxCostUsd":max,"tariff":price.tariff()})));
    }
    input.insert("provider".into(), json!(price.provider));
    Ok(Charge {
        tariff: price.tariff(),
        quantity,
        cost_usd: estimate,
    })
}

// Alibaba's published counting rule: Han characters count twice; SSML markup is
// conservatively counted too. Never let markup lower the preflight bound.
fn bailian_characters(text: &str) -> usize {
    text.chars().map(|c| if matches!(c as u32, 0x2E80..=0x2FFF | 0x3007 | 0x3400..=0x4DBF | 0x4E00..=0x9FFF | 0xF900..=0xFAFF | 0x20000..=0x3FFFF) {2} else {1}).sum()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn r3_d14_bailian_published_character_examples() {
        for (text, count) in [("你好", 4), ("中A文123", 8), ("中文。", 5), ("中 文。", 6)]
        {
            assert_eq!(bailian_characters(text), count);
        }
        let prices = vec![OpPrice {
            source: "published".into(),
            url: "https://www.alibabacloud.com/help/en/model-studio/model-pricing".into(),
            note: String::new(),
            model: "qwen3-tts-flash".into(),
            op: "tts".into(),
            provider: "bailian-tts".into(),
            unit: "bailian_character".into(),
            per_unit: 0.0000114682,
        }];
        let mut input = json!({"text":"你好"}).as_object().unwrap().clone();
        assert_eq!(
            check(&prices, "tts", &mut input, 0.00003)
                .unwrap_err()
                .code(),
            "over_budget"
        );
        assert!(check(&prices, "tts", &mut input, 0.00005).is_ok());
    }
    #[test]
    fn r4_tariff_provenance_missing_placeholder_and_invalid_refuse() {
        let base = json!({"op":"image","provider":"echo-images","model":"gpt-image-2","unit":"image","per_unit":0.02});
        for (source, url, note, valid) in [
            ("", "", "", false),
            ("published", "", "", false),
            ("published", "https://provider.example/prices", "", true),
            ("operator", "", "", false),
            ("operator", "", "SET_BY_OPERATOR", false),
            ("operator", "", "Operator estimate including input", true),
        ] {
            let mut raw = base.clone();
            raw["source"] = json!(source);
            raw["url"] = json!(url);
            raw["note"] = json!(note);
            let price: OpPrice = serde_json::from_value(raw).unwrap();
            let mut input = json!({"prompt":"swatch"}).as_object().unwrap().clone();
            let result = check(&[price], "image", &mut input, 0.02);
            assert_eq!(result.is_ok(), valid, "{source} {note}");
            if !valid {
                assert_eq!(result.unwrap_err().code(), "budget_unpriced");
            }
        }
    }
}
