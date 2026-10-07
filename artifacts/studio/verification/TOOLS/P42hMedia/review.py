#!/usr/bin/env python3
"""Record Main's actual image review; never infer visual success from file existence."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--decision", required=True, choices=("PASS", "FAIL", "BLOCKED"))
    parser.add_argument("--reviewer", required=True)
    parser.add_argument("--observation", required=True,
                        help="Actual visible observations from generated/image/history or all three real healer viewport captures")
    args = parser.parse_args()
    output = args.out.resolve()
    result_path = output / "result.json"
    result = json.loads(result_path.read_text())
    if result.get("runtimeStatus") != "PASS":
        raise SystemExit("Cannot promote an uncompleted or refused runtime qualification")
    if not args.observation.strip() or not args.reviewer.strip():
        raise SystemExit("Reviewer and exact visible observations are required")
    if (output / "visual-review.json").exists():
        raise SystemExit("Visual review already exists; do not silently replace evidence")
    if result["mode"] == "portrait":
        images = ["generated.png", "portrait-applied.png", "portrait-redone.png",
                  "history-applied.png", "history-undone.png", "history-redone.png"]
        checkpoints = ["before", "generated", "undone", "redone", "final"]
        questions = ["The actual generated image depicts a portrait, not a pre-existing fixture or placeholder.",
                     "History captures show applied, undone and redone states; restored image is visibly the same portrait."]
    else:
        images = ["generated.png", "viewport-before.png", "viewport-applied.png", "viewport-undone.png",
                  "studio-before.png", "studio-applied.png", "studio-undone.png"]
        checkpoints = ["before", "generated", "undone", "final"]
        questions = ["The generated image is green robe cloth, not a tint or old texture.",
                     "The applied real healer body visibly uses the green texture; undo visibly restores the original body material.",
                     "All three captures frame the real healer body and are legible; a hidden/cropped body cannot pass."]
    retained = {name: hashlib.sha256((output / name).read_bytes()).hexdigest() for name in images}
    for checkpoint in checkpoints:
        receipt = json.loads((output / ("ledger-" + checkpoint + ".json")).read_text())
        if receipt.get("status") != "PASS":
            raise SystemExit("Missing successful authoritative ledger checkpoint " + checkpoint)
    review = {"row": result["row"], "status": args.decision, "reviewer": args.reviewer,
              "observedUtc": datetime.now(timezone.utc).isoformat(), "observation": args.observation,
              "reviewAssertions": questions, "reviewedImagesSha256": retained,
              "method": "Explicit review of actual retained pixels; not an automated image-quality inference"}
    (output / "visual-review.json").write_text(json.dumps(review, indent=2) + "\n")
    result.update({"status": args.decision, "reason": result["runtimeReason"] + " Visual review: " + args.observation,
                   "visualReview": "visual-review.json"})
    temporary = result_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(result, indent=2) + "\n")
    temporary.replace(result_path)
    print(json.dumps({"row": result["row"], "status": result["status"]}))
    return 0 if args.decision == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
