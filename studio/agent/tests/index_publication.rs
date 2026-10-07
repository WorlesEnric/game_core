//! W-ETOS-04: production graph ingestion through the existing SDK logger, with no new API seam.
mod support;

use etos_sdk::Client;
use gamecore_studio::index::Indexer;
use gamecore_studio::model::IndexDelta;
use gamecore_studio::util::sha256_hex;
use serde_json::{Value, json};
use std::collections::BTreeMap;
use std::sync::Arc;
use support::{AGENT_KEY, FakeNode};

fn graph(nodes: Value, revision: &str) -> Value {
    json!({
        "ref":{"kind":"Definition","assetGuid":"graph-guid","authoringId":"graph-author",
            "definition":format!("Bram@{revision}")},
        "type":"dialogue.graph","name":"Bram",
        "fields":{
            "speaker":{"type":"string","value":"Bram"},
            "speakerEntityId":{"type":"authoringId","value":"bram-entity"},
            "entry":{"type":"int","value":0},
            "nodes":{"type":"object[]","value":nodes},
            "edges":{"type":"object[]","value":[{"from":0,"port":"Next","option":0,"to":1}]}
        }
    })
}

fn delta(revision: u64, nodes: Vec<Value>, base: Option<u64>) -> IndexDelta {
    let mut value = json!({"project":"hollowmere","revision":revision,"nodes":nodes});
    if let Some(base) = base {
        value["baseRevision"] = json!(base);
    }
    match serde_json::from_value(value) {
        Ok(delta) => delta,
        Err(error) => panic!("invalid index delta fixture: {error}"),
    }
}

async fn indexer(
    node: &FakeNode,
    root: &std::path::Path,
) -> (Arc<Indexer>, tokio::task::JoinHandle<()>) {
    let client = match Client::new(&node.url, AGENT_KEY) {
        Ok(client) => client,
        Err(error) => panic!("could not configure fake-node index client: {error}"),
    };
    let indexer = Indexer::new(client, "gamecore-studio", root, 100);
    let task = indexer.start();
    node.until("binding", |state| state.binding.is_some()).await;
    // close() serializes on the logger lock, so each assertion observes an actual delivered SDK batch.
    (indexer, task)
}

async fn finish(index: Arc<Indexer>, task: tokio::task::JoinHandle<()>) {
    let settled = tokio::time::timeout(std::time::Duration::from_secs(10), async {
        while index.pending() != 0 {
            tokio::time::sleep(std::time::Duration::from_millis(20)).await;
        }
        index.close().await;
        if let Err(error) = task.await {
            panic!("index publisher task failed: {error}");
        }
    })
    .await;
    if let Err(error) = settled {
        panic!("index publication did not settle: {error}");
    }
}

fn rows(node: &FakeNode, kind: &str) -> BTreeMap<String, Value> {
    let mut result = BTreeMap::new();
    for trace in &node.lock().traces {
        if trace["kind"] != kind {
            continue;
        }
        let Some(row_key) = trace["key"].as_str() else {
            panic!("logger emitted a non-string row key: {trace}");
        };
        let Some(values) = trace["values"].as_object() else {
            panic!("logger emitted non-object row values: {trace}");
        };
        let row = result.entry(row_key.to_string()).or_insert(json!({}));
        for (key, value) in values {
            row[key] = value.clone();
        }
    }
    result
}

#[tokio::test]
async fn w_etos_04_graph_fields_publish_discoverable_owned_dialogue_nodes() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let (index, task) = indexer(&node, dir.path()).await;
    let owner = "[\"gamecore-unity\",\"project-a\"]";
    let hash = sha256_hex(owner.as_bytes());
    let condition =
        json!({"kind":"Definition","assetGuid":"condition-guid","definition":"ShrineLit@1"});
    let actions = json!({"kind":"Definition","assetGuid":"actions-guid","definition":"Remember@1"});
    let graph = graph(
        json!([
            {"kind":"Line","speaker":"","speakerEntityId":"","text":"Mind the bell.","options":[]},
            {"kind":"Choice","speaker":"Visitor","speakerEntityId":"visitor-entity","text":"Will you help?",
             "condition":condition,"actions":actions,"options":[{"text":"Yes","condition":condition,"hideWhenUnavailable":true}]}
        ]),
        "1",
    );
    index
        .ingest_owned(&delta(1, vec![graph], None), owner)
        .unwrap();
    finish(index, task).await;
    let definitions = rows(&node, "gc_definition");
    let definition = &definitions[&format!("{hash}:Bram@1")];
    assert_eq!(definition["graph"], format!("{hash}:graph-guid"));
    assert_eq!(definition["asset_guid"], "graph-guid");
    assert_eq!(definition["owner"], hash);
    let nodes = rows(&node, "gc_dialogue_node");
    assert_eq!(
        nodes.len(),
        2,
        "only the two serialized nodes, never an invented end node"
    );
    let line = &nodes[&format!("{hash}:graph-guid/0")];
    assert_eq!(line["text"], "Mind the bell.");
    assert_eq!(line["speaker"], format!("{hash}:bram-entity"));
    assert_eq!(line["speaker_name"], "Bram");
    assert_eq!(line["node_index"], 0);
    assert_eq!(line["edges"][0]["to"], 1);
    let choice = &nodes[&format!("{hash}:graph-guid/1")];
    assert_eq!(choice["graph"], definition["graph"]);
    assert_eq!(choice["owner"], hash);
    assert_eq!(choice["speaker"], format!("{hash}:visitor-entity"));
    assert_eq!(choice["conditions"], condition);
    assert_eq!(choice["consequences"], actions);
    assert_eq!(choice["options"][0]["condition"], condition);
    assert_eq!(choice["removed"], false);
}

#[tokio::test]
async fn w_etos_04_graph_revisions_clear_removed_fields_shrink_undo_and_delete() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let owner = "owner-a";
    let hash = sha256_hex(owner.as_bytes());
    let original = graph(
        json!([
            {"kind":"Line","text":"Before","condition":{"kind":"Definition","definition":"Condition@1"},"actions":{"kind":"Definition","definition":"Actions@1"}},
            {"kind":"End","text":""}
        ]),
        "1",
    );
    let (index, task) = indexer(&node, dir.path()).await;
    index
        .ingest_owned(&delta(1, vec![original.clone()], None), owner)
        .unwrap();
    finish(index, task).await;

    let (index, task) = indexer(&node, dir.path()).await;
    let edited = graph(
        json!([{"kind":"Line","speaker":"Narrator","text":"After"}]),
        "2",
    );
    index
        .ingest_owned(&delta(2, vec![edited], Some(1)), owner)
        .unwrap();
    finish(index, task).await;
    let nodes = rows(&node, "gc_dialogue_node");
    let line = &nodes[&format!("{hash}:graph-guid/0")];
    assert_eq!(line["text"], "After");
    assert!(line["conditions"].is_null());
    assert!(line["consequences"].is_null());
    assert!(
        line["speaker"].is_null(),
        "named narrator must not retain the former entity link"
    );
    assert_eq!(nodes[&format!("{hash}:graph-guid/1")]["removed"], true);
    assert_eq!(
        rows(&node, "gc_definition")[&format!("{hash}:Bram@1")]["removed"],
        true
    );

    let (index, task) = indexer(&node, dir.path()).await;
    index
        .ingest_owned(&delta(3, vec![original.clone()], Some(2)), owner)
        .unwrap();
    finish(index, task).await;
    assert_eq!(
        rows(&node, "gc_dialogue_node")[&format!("{hash}:graph-guid/1")]["removed"],
        false
    );
    let (index, task) = indexer(&node, dir.path()).await;
    let mut removed = delta(4, vec![], Some(3));
    removed.removals = vec![serde_json::from_value(original["ref"].clone()).unwrap()];
    index.ingest_owned(&removed, owner).unwrap();
    finish(index, task).await;
    assert!(
        rows(&node, "gc_dialogue_node")
            .values()
            .all(|row| row["removed"] == true)
    );
}

#[tokio::test]
async fn w_etos_04_project_open_after_restart_removes_stale_graphs_only_for_owner() {
    let node = FakeNode::start().await;
    let dir = tempfile::tempdir().unwrap();
    let (index, task) = indexer(&node, dir.path()).await;
    let graph = graph(json!([{"kind":"Line","text":"Retained"}]), "1");
    for owner in ["owner-a", "owner-b"] {
        index
            .ingest_owned(&delta(8, vec![graph.clone()], None), owner)
            .unwrap();
    }
    finish(index, task).await;
    let (index, task) = indexer(&node, dir.path()).await;
    // A reopened Editor may have a reset local revision and a graph deleted while disconnected.
    index
        .ingest_owned(&delta(1, vec![], None), "owner-a")
        .unwrap();
    finish(index, task).await;
    let nodes = rows(&node, "gc_dialogue_node");
    assert_eq!(
        nodes[&format!("{}:graph-guid/0", sha256_hex(b"owner-a"))]["removed"],
        true
    );
    assert_eq!(
        nodes[&format!("{}:graph-guid/0", sha256_hex(b"owner-b"))]["removed"],
        false
    );
}
