# OPUS-MT test fixtures

Real tokenizer files from the OPUS-MT **zh-en** model (Helsinki-NLP), used to test
`OpusMtTokenizer` deterministically without downloading the full ONNX model.

| File        | Source repo                           | Size  |
|-------------|---------------------------------------|-------|
| source.spm  | `Helsinki-NLP/opus-mt-zh-en`          | ~0.8M |
| vocab.json  | `Helsinki-NLP/opus-mt-zh-en`          | ~1.7M |
| config.json | `Helsinki-NLP/opus-mt-zh-en`          | ~1.4K |

Mirror download URL (same bytes):
`https://hf-mirror.com/Helsinki-NLP/opus-mt-zh-en/resolve/main/{source.spm|vocab.json|config.json}`

License: OPUS-MT models are published under **CC-BY-4.0** (see
https://huggingface.co/Helsinki-NLP/opus-mt-zh-en). The full-precision ONNX weights
(`onnx/encoder_model.onnx`, `onnx/decoder_model.onnx`, `onnx/decoder_with_past_model.onnx`)
and int8 quantized builds (`onnx/*_quantized.onnx`) are downloaded at runtime by
`Centurion models install zh-en` and are intentionally not committed.
