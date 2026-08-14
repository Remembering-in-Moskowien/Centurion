namespace Centurion.Core.Models;

public class EmbeddedResources
{
    #region Diarize Script

    public const string DiarizeServiceScript = """
                                               #!/usr/bin/env python3
                                               import sys
                                               import json
                                               import signal
                                               from diarize import diarize

                                               def main():
                                                   signal.signal(signal.SIGINT, lambda s, f: sys.exit(0))
                                                   signal.signal(signal.SIGTERM, lambda s, f: sys.exit(0))

                                                   while True:
                                                       line = sys.stdin.readline()
                                                       if not line:
                                                           break
                                                       try:
                                                           data = json.loads(line)
                                                       except json.JSONDecodeError as e:
                                                           sys.stdout.write(json.dumps({'error': f'JSON parse error: {e}'}) + '\n')
                                                           sys.stdout.flush()
                                                           continue

                                                       audio_file = data.get('audio_file')
                                                       if not audio_file:
                                                           sys.stdout.write(json.dumps({'error': 'Missing audio_file'}) + '\n')
                                                           sys.stdout.flush()
                                                           continue

                                                       try:
                                                           num_speakers = data.get('num_speakers', 0)
                                                           threshold = data.get('threshold', 0.5)
                                                           result = diarize(audio_file, 
                                                                            num_speakers=num_speakers if num_speakers > 0 else None,
                                                                            threshold=threshold)
                                                           segments = [{'start': seg.start, 'end': seg.end, 'speaker': seg.speaker} 
                                                                       for seg in result.segments]
                                                           sys.stdout.write(json.dumps({'segments': segments}) + '\n')
                                                       except Exception as e:
                                                           import traceback
                                                           err = {'error': str(e), 'traceback': traceback.format_exc()}
                                                           sys.stdout.write(json.dumps(err) + '\n')
                                                       sys.stdout.flush()

                                               if __name__ == '__main__':
                                                   main()
                                               """;

    #endregion

    #region Sat Split Script

    public const string SatSplitServiceScript = """
                                                #!/usr/bin/env python3
                                                import sys
                                                import json
                                                import signal
                                                import re
                                                from wtpsplit import SaT

                                                # 全局加载模型（只一次）
                                                sat = SaT("sat-3l-sm", ort_providers=["CPUExecutionProvider"])

                                                TIME_GAP_THRESHOLD = 1500

                                                def split_by_time_gap(words, gap_threshold_ms):
                                                    if not words:
                                                        return []
                                                    words = sorted(words, key=lambda x: x['start'])
                                                    segments = []
                                                    current_seg = [words[0]]
                                                    for i in range(1, len(words)):
                                                        gap = words[i]['start'] - words[i-1]['end']
                                                        if gap > gap_threshold_ms:
                                                            segments.append(current_seg)
                                                            current_seg = [words[i]]
                                                        else:
                                                            current_seg.append(words[i])
                                                    if current_seg:
                                                        segments.append(current_seg)
                                                    return segments

                                                def split_by_punctuation(words):
                                                    if not words:
                                                        return []
                                                    sub_segments = []
                                                    current = []
                                                    for w in words:
                                                        current.append(w)
                                                        if w['text'].strip().endswith(('.', '!', '?')):
                                                            sub_segments.append(current)
                                                            current = []
                                                    if current:
                                                        sub_segments.append(current)
                                                    return sub_segments if sub_segments else [words]

                                                def process_words(words, max_len, target_len, spread):
                                                    if not words:
                                                        return []
                                                    time_segments = split_by_time_gap(words, TIME_GAP_THRESHOLD)
                                                    all_sentences = []

                                                    for seg in time_segments:
                                                        sub_segments = split_by_punctuation(seg)
                                                        for sub in sub_segments:
                                                            if not sub:
                                                                continue
                                                            full_text = ' '.join([w['text'] for w in sub])
                                                            if not full_text.strip():
                                                                continue

                                                            try:
                                                                sentences = sat.split(
                                                                    full_text,
                                                                    max_length=max_len,
                                                                    prior_type="gaussian",
                                                                    prior_kwargs={"target_length": target_len, "spread": spread}
                                                                )
                                                            except Exception as e:
                                                                print(f"SAT split error: {e}", file=sys.stderr)
                                                                continue

                                                            word_positions = []
                                                            pos = 0
                                                            for w in sub:
                                                                start = pos
                                                                end = start + len(w['text'])
                                                                word_positions.append((start, end, w))
                                                                pos = end + 1

                                                            search_start = 0
                                                            for sent in sentences:
                                                                idx = full_text.find(sent, search_start)
                                                                if idx == -1:
                                                                    stripped = sent.strip()
                                                                    idx = full_text.find(stripped, search_start)
                                                                    if idx != -1:
                                                                        sent_end = idx + len(stripped)
                                                                        sent = full_text[idx:sent_end]
                                                                    else:
                                                                        search_start += 1
                                                                        continue
                                                                else:
                                                                    sent_end = idx + len(sent)

                                                                overlap = []
                                                                for s, e, w in word_positions:
                                                                    if s < sent_end and e > idx:
                                                                        overlap.append(w)

                                                                if overlap:
                                                                    all_sentences.append({
                                                                        'text': sent,
                                                                        'start': min(w['start'] for w in overlap),
                                                                        'end': max(w['end'] for w in overlap)
                                                                    })
                                                                search_start = sent_end

                                                    all_sentences.sort(key=lambda x: x['start'])
                                                    return all_sentences

                                                def main():
                                                    signal.signal(signal.SIGINT, lambda s, f: sys.exit(0))
                                                    signal.signal(signal.SIGTERM, lambda s, f: sys.exit(0))

                                                    while True:
                                                        line = sys.stdin.readline()
                                                        if not line:
                                                            break
                                                        try:
                                                            data = json.loads(line)
                                                        except json.JSONDecodeError as e:
                                                            sys.stdout.write(json.dumps({'error': f'JSON parse error: {e}'}) + '\n')
                                                            sys.stdout.flush()
                                                            continue

                                                        try:
                                                            words = data.get('words', [])
                                                            if not words:
                                                                sys.stdout.write(json.dumps({'sentences': []}) + '\n')
                                                                sys.stdout.flush()
                                                                continue

                                                            max_len = data.get('max_length', 80)
                                                            target_len = data.get('target_length', 50)
                                                            spread = data.get('spread_range', 10)
                                                            sentences = process_words(words, max_len, target_len, spread)
                                                            sys.stdout.write(json.dumps({'sentences': sentences}) + '\n')
                                                        except Exception as e:
                                                            import traceback
                                                            err = {'error': str(e), 'traceback': traceback.format_exc()}
                                                            sys.stdout.write(json.dumps(err) + '\n')
                                                        sys.stdout.flush()

                                                if __name__ == '__main__':
                                                    main()
                                                """;

    #endregion

    public static string GetScript(string fileName)
    {
        return fileName switch
        {
            "sat_split_service.py" => SatSplitServiceScript,
            "diarize_service.py" => DiarizeServiceScript,
            _ => throw new ArgumentException($"Unknown script: {fileName}")
        };
    }
}