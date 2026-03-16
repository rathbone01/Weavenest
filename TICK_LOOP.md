# Jeremy's Tick Loop

Jeremy doesn't run continuously — he exists in discrete **ticks**. Between ticks he is inert. Each tick is one moment of awareness.

## Timing

- Ticks fire on a fixed interval (`MindSettings.TickIntervalSeconds`, default 30s)
- A `SemaphoreSlim(1,1)` guard ensures only one tick runs at a time — if a tick is still processing when the next interval fires, the new tick is **skipped** (not queued)
- On startup, Jeremy waits 5 seconds before the first tick to let the app fully initialize

---

## Tick Phases

```
startup
  │
  ├─ Load initial emotional state from DB → push to UI
  └─ (unprocessed message recovery — partial, see note below)

every N seconds
  │
  ├─ [skip if previous tick still running]
  │
  └─ ExecuteTickAsync
       │
       ├── 1. Snapshot emotion BEFORE
       ├── 2. Drain human message queue
       ├── 3. Assemble system prompt + stimulus
       ├── 4. Tool loop (Ollama calls)
       │     ├── [optional] Reflection call
       │     └── ...
       ├── 5. Snapshot emotion AFTER
       ├── 6. Save TickLog to DB
       ├── 7. Add conscious content to short-term memory
       └── 8. Publish results → UI
```

---

## Phase Detail

### 1. Emotion Snapshot (Before)
Loads the current `EmotionalState` row from SQL Server. This is the baseline — used later to diff against the post-tick state and show how Jeremy's emotions shifted.

### 2. Drain Human Message Queue
Pulls **all** messages the human sent since the last tick from an in-memory `ConcurrentQueue<string>`. If three messages arrived while Jeremy was thinking, he sees all three in this tick — not one per tick.

Each message is:
- Added to **short-term memory** as a `"human"` entry
- Marked `Processed = true` in the `HumanMessages` DB table (for crash recovery)

### 3. Assemble Prompt + Stimulus
**System prompt** is built fresh each tick by `PromptAssemblyService`:
- Jeremy's persona and output rules
- Current emotional state in natural language
- Recent long-term memories retrieved by tag relevance
- Short-term memory buffer (last N entries)

**Stimulus** is the user-facing trigger message:
- 0 messages → `"No new input. This is an idle tick. Reflect, process memories, or rest."`
- 1 message → `The human said: "..."`
- N messages → numbered list of all messages

### 4. Tool Loop
This is where Jeremy actually thinks. It runs up to `MaxToolIterations` (10) times but typically exits much sooner.

```
iteration 0:
  → Ollama called with system prompt + stimulus + all tools
  ← Response has: thinking (subconscious) + content (inner monologue) + optional tool_calls

  if no tool_calls:
    → consciousContent = content
    → DONE (1 Ollama call)

  if tool_calls present:
    → dispatch each tool
    → append tool results to message history
    → if "continue" tool was NOT called → BREAK
    → if "continue" tool WAS called → loop to iteration 1

iteration 1, 2, ...:
  → same as iteration 0 but with accumulated tool result history
  → Jeremy sees what his tools returned and can act on them
  → still only continues if he calls "continue" again
```

**Thinking (subconscious)** from every Ollama call is captured and pushed to the UI immediately — it doesn't wait for the tick to finish.

**After the loop**, if `consciousContent` is still null (tools were called but the model wrote no prose), a **reflection call** is made:
- A guidance message is prepended: `[Inner monologue phase — no tools available. Write plain text only.]`
- Tools are omitted from the request (`tools: null`)
- Jeremy writes his inner monologue reaction to what he just did
- This becomes `consciousContent`

#### Tools Available
| Tool | Purpose |
|------|---------|
| `speak` | Send a message the human can see — the ONLY way to communicate |
| `store_memory` | Save something to long-term memory |
| `recall` | Search long-term memory by tags |
| `update_emotion` | Adjust emotional state (delta values) |
| `reflect` | Trigger deeper self-examination on a topic |
| `link_memories` | Associate two memories |
| `supersede_memory` | Replace an old belief with a new one |
| `web_search` | Search the internet |
| `web_fetch` | Fetch a URL |
| `continue` | Request another tool iteration this tick |

#### The `continue` Tool
Without `continue`, Jeremy gets **one round of tool calls per tick** — he thinks, acts, then the loop ends and a reflection call gives him his conscious voice. If he needs to chain actions (e.g. search → store result → speak), he calls `continue` to get another iteration.

### 5. Emotion Snapshot (After)
Loads emotional state again. If Jeremy called `update_emotion` during the tick, this will differ from the before snapshot. Both are saved to the tick log and shown in the UI emotion bars.

### 6. Save TickLog
Every tick writes a `TickLog` row to SQL Server containing:
- Subconscious content (thinking)
- Conscious content (inner monologue)
- Spoke content (what Jeremy said aloud, if anything)
- Emotional state before/after (JSON)
- Tool call log (JSON array of `{Tool, Arguments, Result}`)

### 7. Short-Term Memory
The conscious content is added to the rolling short-term memory buffer as a `"conscious"` entry tagged with the context topics. This means future ticks include Jeremy's own recent thoughts in his context — he has continuity.

### 8. Publish to UI
`MindStateService` fires events that Blazor components subscribe to:
- `OnTickCompleted` → SubconsciousPanel (shows raw thinking)
- `OnConsciousThought` → ConsciousStreamPanel (inner monologue)
- `OnSpoke` → ChatPanel (spoken message appears in chat)
- `OnEmotionChanged` → EmotionalStateDisplay (updates emotion bars)

---

## Output Types

| Layer | Visible to human? | Source |
|-------|------------------|--------|
| Subconscious (thinking) | No — debug panel only | `message.thinking` field (Ollama native) or `<think>` tags |
| Conscious (inner monologue) | No — stream panel only | `message.content` after stripping tags |
| Spoken | **Yes** | `speak` tool call |

The human only ever sees what Jeremy explicitly routes through `speak`. Everything else is Jeremy's private mind.

---

## Content Sanitization
`ThinkTagParser` cleans raw Ollama output before it reaches the UI:
- Strips `<think>` / `</think>` tags (and orphaned `</think>` Ollama sometimes leaves in content)
- Strips `<tool_call>...</tool_call>` blocks — qwen3's fallback syntax when it wants to call a tool but none are registered

---

## Crash Recovery (Partial)
On startup, `ConsciousnessLoopService.StartAsync` queries for unprocessed `HumanMessages` in the DB. The recovery loop is currently scaffolded but doesn't re-enqueue them into the in-memory queue — messages sent before a crash will not be replayed. This is a known gap.
