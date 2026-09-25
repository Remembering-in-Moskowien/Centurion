---
title: Translate
---

# 🌐 `translate` — Translation, Without the Timeline Drama

Got an intermediate file in a language you don't want? `translate` rewrites the **text only** — the timeline, the word details, everything spatial stays untouched. It's text alignment, not a remix. 🎯

```bash
Centurion translate <CENTURION_FILE> -t <LANG> [options]
```

## Options

- `<CENTURION_FILE>` — input Centurion intermediate file (.centurion.json) 📄
- `-t, --target-language <LANG>` — target language code, e.g. `zh`, `en`, `ja` (required)
- `-o, --output <OUTPUT_FILE>` — output intermediate path (defaults to `<input>.translated.centurion.json`)
- `--source-language <LANG>` — source language (auto-detected when omitted)
- `-s, --strategy <STRATEGY>` — translation strategy, `llm` (default)
- `--model <MODEL>` — LLM model (provider default: OpenAI `gpt-4o-mini`, DeepSeek `deepseek-chat`, Ollama `llama3.1`…)
- `--api-key <KEY>` — API key for the chosen provider; **omit it to fall back to local Ollama**
- `--llm-provider <PROVIDER>` — pick the API: `openai` · `deepseek` · `moonshot` · `zhipu` · `openrouter` · `groq` · `siliconflow` · `dashscope` · `ark` · `azure` · `ollama` (default `auto`)
- `--llm-base-url <URL>` — override the provider's default endpoint (e.g. a self-hosted gateway)
- `--glossary <FILE>` — glossary JSON: `{ "source": "target" }` or `[{ "source":…, "target":… }]` 📚
- `--target-script <FILE>` — target-language script (one line per subtitle) 📝
- `-b, --bilingual` — output bilingual subtitles (source on top, translation below) 🈳
- `-k, --karaoke` — word-level `\K` timestamps for translations 🎵

## The magic of target-script alignment ✨

Hand it an **official target-language script** (subtitles count matching) and `translate` performs **pure 1:1 text alignment** — line N of the script becomes line N of the subtitles, no LLM involved. Official wording, guaranteed. If the counts don't match, it warns you and translates with the script as a wording reference instead.

## Translated karaoke timestamps ⏱️

No word-level timing exists for a translation — so Centurion **builds one**:

- **Time interpolation**: the sentence's `[start, end]` is redistributed across the translated words
- **Long-syllable bias**: longer words (more syllables / more characters) get proportionally more time — no more "a" and "extraordinary" fighting over the same millisecond 😄
- A lead-in pause (`\K`) opens each line, matching the classic karaoke rhythm

## Examples 🧪

```bash
# Convert first, then translate
Centurion convert subtitles.srt
Centurion translate subtitles.centurion.json -t zh --api-key sk-xxx --model gpt-4o-mini

# DeepSeek (cheap & fast)
Centurion translate subtitles.centurion.json -t zh --llm-provider deepseek --api-key sk-xxx

# Moonshot / Kimi
Centurion translate subs.centurion.json -t zh --llm-provider moonshot --api-key sk-xxx --model moonshot-v1-8k

# Local Ollama (no key needed)
Centurion translate subtitles.centurion.json -t ja

# Glossary + official script + bilingual + karaoke
Centurion translate subs.centurion.json -t zh --glossary terms.json --target-script official.txt -b -k

# Build the final bilingual karaoke ASS
Centurion build subs.translated.centurion.json
```

> 🤖 **LLM providers** — Centurion speaks the **OpenAI-compatible dialect** everyone uses. Set `--llm-provider` (or just `--llm-base-url`) and the right defaults are applied automatically; `auto` infers the provider from the endpoint host, or falls back to OpenAI when an API key is present and to local Ollama otherwise. Provider names are forgiving: `ds`, `kimi`, `glm`, `silicon`, `aliyun`, `volcano`… all work. The same options power LLM sentence splitting in `spawn` (`-s llm --llm-provider …`).

> 💡 **Bilingual layout**: source line rides on top (`Default` style), translation hugs the bottom (`Sub` style) — a layout borrowed from classic dual-language fansubs.

> 🧾 Every run saves a `<output>.translated.centurion.json` — full config, per-sentence translations, glossary & script load state, and any warnings. Perfect for debugging "why did *that* line come out like that". Chain it into `dub` or `build` directly.
