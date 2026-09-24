# AI provider fixtures

Response bodies the 009 adapter tests (checkpoint 3) feed to a fake
`HttpMessageHandler`. The tests never reach the network, and no test calls a real AI API.

## Provenance: every file here is hand-written, none is captured

There was no API key and the development environment's egress is blocked, so nothing
could be captured. Each file was written by hand on **2026-09-24** from the provider's
documented response shape. Field names, stop reasons and the usage fields follow the
documentation. **The ids, texts and token counts are illustrative.** Replace each file with
a real capture (same file name, a short prompt, nothing personal in it; responses carry no
key) and update this table.

| File | Provider · request | Covers | Status |
|---|---|---|---|
| `anthropic-messages-end-turn.json` | Anthropic · `POST v1/messages` | an empty `thinking` block (adaptive thinking on `claude-opus-5`) and two `text` blocks, `end_turn`, `usage.input_tokens` / `output_tokens` | hand-written from docs |
| `anthropic-messages-max-tokens.json` | Anthropic · `POST v1/messages` | a truncated answer, `stop_reason: max_tokens` | hand-written from docs |
| `anthropic-messages-refusal.json` | Anthropic · `POST v1/messages` | an HTTP 200 with `stop_reason: refusal` and its usage | hand-written from docs |
| `openai-chat-completion-stop.json` | OpenAI · `POST v1/chat/completions` | `finish_reason: stop`, `usage.prompt_tokens` / `completion_tokens` | hand-written from docs |
| `openai-chat-completion-length.json` | OpenAI · `POST v1/chat/completions` | a truncated answer, `finish_reason: length` | hand-written from docs |
| `openai-chat-completion-refusal.json` | OpenAI · `POST v1/chat/completions` | `message.content: null` with `message.refusal` set | hand-written from docs |

Error bodies (`{"type":"error","error":{...}}` for Anthropic, `{"error":{...}}` for
OpenAI) and malformed bodies are inline strings in the tests, not files.
