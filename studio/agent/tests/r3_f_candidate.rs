#![allow(clippy::unwrap_used, clippy::expect_used)]
//! R3-F companion/core scope and tint handoff regressions.
use gamecore_studio::candidate::{CatalogContext, Evaluation, Fetched, evaluate};
use gamecore_studio::schema::ChangeSetSchema;
use gamecore_studio::util::{catalog_revision, sha256_hex};
use serde_json::{Value, json};

fn evaluate_json(candidate: Value, catalog: &Value) -> Evaluation {
    let bytes = serde_json::to_vec(&candidate).unwrap();
    evaluate(
        &[Fetched {
            reference: "candidate".into(),
            sha256: sha256_hex(&bytes),
            bytes,
        }],
        candidate["id"].as_str().unwrap(),
        &ChangeSetSchema::load(None).unwrap(),
        CatalogContext {
            index_slice: None,
            revision: &catalog_revision(catalog),
            catalog: Some(catalog),
        },
    )
}

fn catalog(tool_scopes: Value, type_scopes: Value) -> Value {
    json!({"objectTypes":[{"typeId":"entity.instance","scopes":type_scopes}],"tools":[{
        "id":"entity.applyOverride","targetType":"entity.instance","scopes":tool_scopes,
        "targetKinds":["Entity"],"targetRequired":true,
        "args":[{"name":"field","required":true},{"name":"value"}]
    }]})
}
fn candidate() -> Value {
    json!({"schema":"gamecore.studio.changeset/1","id":"cs_01ARZ3NDEKTSV4RRFFQ69G5FAV",
        "intent":{"text":"robe","origin":"agent"},"operations":[{
        "opId":"tint","tool":"entity.applyOverride","target":{"kind":"Entity","authoringId":"abc"},
        "args":{"field":"tint","value":"#123456"},"preconditions":"none"}]})
}

#[test]
fn d5_singleton_intersection_normalizes_raw_and_typed_with_nonblocking_evidence() {
    let Evaluation::Valid(v) = evaluate_json(
        candidate(),
        &catalog(json!(["Instance", "Definition"]), json!(["Instance"])),
    ) else {
        panic!("singleton should pass")
    };
    assert_eq!(v.raw["operations"][0]["target"]["scope"], "Instance");
    assert_eq!(
        serde_json::to_value(v.change_set.operations[0].target.as_ref().unwrap().scope).unwrap(),
        "Instance"
    );
    assert_eq!(v.warnings[0].code, "ScopeInferred");
    assert_eq!(
        v.warnings[0].data.as_ref().unwrap(),
        json!({"inferred":true,"scope":"Instance"})
            .as_object()
            .unwrap()
    );
}

#[test]
fn d5_ambiguous_empty_and_explicit_disallowed_scopes_refuse() {
    for (tool, ty, explicit) in [
        (
            json!(["Instance", "Definition"]),
            json!(["Instance", "Definition"]),
            false,
        ),
        (json!(["Definition"]), json!(["Instance"]), false),
        (json!(["Instance", "Definition"]), json!(["Instance"]), true),
    ] {
        let mut c = candidate();
        if explicit {
            c["operations"][0]["target"]["scope"] = json!("Definition");
        }
        let Evaluation::Invalid(d) = evaluate_json(c, &catalog(tool, ty)) else {
            panic!("scope should refuse")
        };
        assert!(d.iter().any(|d| d.code == "ScopeNotAllowed"));
    }
}

#[test]
fn d4_instance_tint_contract_precheck() {
    for value in [json!("#123456ff"), json!([1, 0, 0]), json!("green")] {
        let mut c = candidate();
        c["operations"][0]["args"]["value"] = value.clone();
        let Evaluation::Invalid(d) =
            evaluate_json(c, &catalog(json!(["Instance"]), json!(["Instance"])))
        else {
            panic!("invalid tint should refuse")
        };
        assert!(d.iter().any(|d| d.code == "InvalidArgs"
            && d.data.as_ref().unwrap()["contract"] == "GP-ENT-004"
            && d.data.as_ref().unwrap()["actual"] == value));
    }
    for value in [json!(""), json!("#abcdef")] {
        let mut c = candidate();
        c["operations"][0]["args"]["value"] = value;
        assert!(matches!(
            evaluate_json(c, &catalog(json!(["Instance"]), json!(["Instance"]))),
            Evaluation::Valid(_)
        ));
    }
}

#[test]
fn d4_d5_retained_robe_has_same_refusals_as_core() {
    let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../artifacts/studio/workflows/P3.2/runs");
    let fixture: Value =
        serde_json::from_str(include_str!("fixtures/r3_f/robe-verdicts.json")).unwrap();
    for case in fixture["cases"].as_array().unwrap() {
        let path = case["path"].as_str().unwrap();
        let expected = case["code"].as_str().unwrap();
        let mut c: Value =
            serde_json::from_slice(&std::fs::read(root.join(path)).unwrap()).unwrap();
        // Retain the original operation exactly; media operations are tested in their own lane.
        let op = c["operations"]
            .as_array()
            .unwrap()
            .iter()
            .find(|o| o["tool"] == "entity.applyOverride")
            .unwrap()
            .clone();
        c["operations"] = json!([op]);
        c.as_object_mut().unwrap().remove("artifacts");
        let Evaluation::Invalid(d) = evaluate_json(c, &fixture["catalog"]) else {
            panic!("retained robe must refuse")
        };
        assert!(d.iter().any(|d| d.code == expected), "{d:?}");
    }
}
