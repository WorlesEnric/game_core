#![allow(clippy::unwrap_used, clippy::expect_used)]
//! Shared retained-candidate regressions for the companion and core.
use gamecore_studio::candidate::{CatalogContext, Evaluation, Fetched, evaluate, reaskable};
use gamecore_studio::schema::ChangeSetSchema;
use gamecore_studio::util::{catalog_revision, sha256_hex};
use serde_json::Value;

fn retained() -> Value {
    serde_json::from_str(include_str!("../../../artifacts/studio/verification/W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/real-candidate.json")).unwrap()
}
fn evaluate_fixture(context: &Value) -> Evaluation {
    let c = retained()["changeSet"].clone();
    let bytes = serde_json::to_vec(&c).unwrap();
    evaluate(
        &[Fetched {
            reference: "retained".into(),
            sha256: sha256_hex(&bytes),
            bytes,
        }],
        c["id"].as_str().unwrap(),
        &ChangeSetSchema::load(None).unwrap(),
        CatalogContext {
            revision: &catalog_revision(&context["catalog"]),
            catalog: Some(&context["catalog"]),
            index_slice: Some(&context["index"]),
        },
    )
}
#[test]
fn p42_live_01_retained_generic_move_infers_indexed_instance() {
    let ctx: Value = serde_json::from_str(include_str!("fixtures/r4_c/context.json")).unwrap();
    let Evaluation::Valid(v) = evaluate_fixture(&ctx) else {
        panic!("{:?}", evaluate_fixture(&ctx))
    };
    assert_eq!(v.raw["operations"][0]["target"]["scope"], "Instance");
    assert_eq!(
        serde_json::to_value(v.change_set.operations[0].target.as_ref().unwrap().scope).unwrap(),
        "Instance"
    );
    assert!(v.warnings.iter().any(|d| d.code == "ScopeInferred"));
}
#[test]
fn p42_live_01_missing_or_ambiguous_index_refuses_and_reasks() {
    let mut ctx: Value = serde_json::from_str(include_str!("fixtures/r4_c/context.json")).unwrap();
    for node in ctx["index"]["nodes"].as_array_mut().unwrap() {
        node["ref"].as_object_mut().unwrap().remove("scope");
    }
    let Evaluation::Invalid(d) = evaluate_fixture(&ctx) else {
        panic!("missing type must refuse")
    };
    assert!(d.iter().any(|d| d.code == "ScopeNotAllowed"));
    assert!(reaskable(&d));
}
