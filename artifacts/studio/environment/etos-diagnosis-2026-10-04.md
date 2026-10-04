# real_node failure: diagnosis (2026-10-04, after the verify run of 20:55 UTC)

This note is kept outside `etos-verify-2026-10-04/`, because every `verify.sh` run replaces that
directory. The 20:55 UTC verify run (game_core `22e0cc9`, etos `5fa113b`) passed every etos check
(node, image, describe, tts, realtime, 3d refusal, hello through the proxy) and **failed** the
companion's real-node test (`studio/agent/tests/real_node.rs`). Both scenarios timed out with the
designer request still `running` (`taskStatus` `starting`, then `queued`); see `real-node.txt` in
git at `b08321d`. What was
established afterwards, by hand on the host (`ETOS_ROOT=~/.local/share/etos-studio`):

1. **Earlier passes were masking a refusal.** Before etos `5fa113b` the same test passed (2 of 2)
   although every request ended `failed` at `phase: open` with
   `` `gc-designer` is not a valid worker name; use a lowercase letter, then lowercase letters, digits or _ (at most 64) ``:
   etrg's `open_view` checked worker names with the RG kind-name rule. Fixed in etos `5fa113b`
   (`check_worker`, the node's rule). Since then `POST /tasks` opens the task (task ids
   `t658160503cb112b318184111`, `t131be22ff26f0ef9309e9e9c`).
2. **The opened task stayed `starting`.** `etos task show t658160503cb112b318184111`: `status=starting`,
   `turns: 0`, for over 10 minutes; its `logs/<task>/` directory existed but was empty (no
   `image.log`), no container and no `docker` child of etosd. The task is launched inline in the
   `POST /tasks` handler (etos `crates/etnode/src/task.rs` `create` → `advance_now` → `launch`); the
   companion's SDK client gives up after 30 s (`studio/agent/vendor/etos-sdk/src/client.rs`, default
   timeout). The likely cause is that the handler future was dropped with the request while the host
   was heavily loaded (load average above 200 during the run), leaving the task `starting`, which
   only node start-up recovery resumes. Not proven beyond this observation.
3. **Restarting etosd recovered it.** `systemctl --user restart etosd.service` →
   `tasks recovered report=Recovery { kept: [], relaunched: [TaskId(t658160503cb112b318184111)], … }`;
   within 30 s the task was `running` in container `etos-gc-designer-t658160503cb112b318184111`
   (image `localhost/etos-task:15d2f8ac8327e9c751d52f4bdca1c23e`, the node's layer over
   `localhost/gc-designer:current`, network offline). So the Docker worker path works.
4. **The worker's model is down.** The running task's turn then failed on the model:
   `retrying model=echo/claude-opus-5-5 … reason=HTTP 503: auth_unavailable: no auth available (providers=claude, model=claude-opus-5-5); …`
   and `model marked degraded model=echo/claude-opus-5-5` (Echo-side, see
   [provider-probe-2026-10-04.md](provider-probe-2026-10-04.md)). Both test tasks were then
   cancelled (`etos task cancel`).

## Resolution (2026-10-05)

- Item 2, fixed in etos:
  - `5cec594` (etnode): `Node::open_task`, and the start in `TaskManager::open`, run on tasks of
    their own. Test: `a_dropped_open_still_starts_the_task` (real containers).
  - `278ef9c` (etagents): `POST /tasks` runs to its end, so the request→task row is always written.
    Test: `a_dropped_task_open_is_recorded_and_found_again`, which fails without the fix.
- Item 4: the workers now use `echo/gpt-6-sol`
  ([provider-probe-2026-10-05.md](provider-probe-2026-10-05.md)).
- A third problem appeared once tasks ran: a cancel settled `failed` because the worker's closing
  record arrived before etos marked the task cancelled. Fixed in the companion (game_core
  `0e5eecd`).
