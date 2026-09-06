# Thumbnail並列生成 Raspberry Pi実機測定

## 結論

- `Media.MaximumConcurrentThumbnailJobs` の既定値は`2`とする。
- `kurastorage-worker.service`に`CPUQuota=125%`を設定する。
- `THUMBNAIL`と`PDF_THUMBNAIL`だけを並列枠で実行し、動画Low/Medium派生生成は含めない。
- 並列2は同じCPU上限の直列1に対し、全12件の完了時間を62.699秒から56.033秒へ10.6%短縮した。
- 並列4以上の追加改善は小さく再現性が低いうえ、並列6と並列8の反復でCPU余力25%を下回ったため採用しない。

## 測定環境

| 項目 | 値 |
| --- | --- |
| Server | Raspberry Pi 4 Model B、8 GB、64-bit ARM Linux |
| Storage | USB 3接続HDD上のKuraStorage領域 |
| Database | PostgreSQL 17 |
| Release | `0.16.0-thumbnail-benchmark`相当の`linux-arm64`自己完結build |
| Artifact SHA-256 | `e1a9cec2899f2f5dde816dc14dbfadc966c614ac05207c1f3c361592f3b161f8` |
| Thumbnail | 長辺512 px、WebP quality 75 |
| Job構成 | JPEG 4件、MP4 4件、PDF 4件の合12件 |
| Foreground負荷 | 手動Upload 1件、Backup Upload 1件、一覧80回、Original動画256 KiB Range 40回 |
| Request間隔 | 一覧500 ms、Range 150 ms |
| OS観測 | `vmstat` 1秒間隔、`vcgencmd`、systemd cgroup |

測定中の一覧とRangeは順次requestとし、APIを無間隔で連打する人工的なCPU飽和は除外した。Rangeは6秒の1080p H.264/AAC元動画に対して行い、失敗や中断の増加をrebuffer相当の異常として扱った。

## 並列数の比較

最終の安全設定`CPUQuota=125%`での比較は次のとおり。

| 並列数 | 最大running/token | 最初のREADY | 全件完了 | 成功/失敗 | 一覧p95 | Range p95 | 平均CPU余力 | 平均I/O wait | swap in/out | thermal |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | 1/1 | 25.094 s | 62.699 s | 12/0 | 157.339 ms | 156.015 ms | 25.69% | 1.09% | 0/0 | `0x0` |
| 2 | 2/2 | 25.852 s | 56.033 s | 12/0 | 134.762 ms | 138.607 ms | 33.28% | 1.29% | 0/0 | `0x0` |

並列2の一覧p95は直列比14.4%改善、Range p95は11.2%改善し、Foreground p95悪化20%未満を満たした。最小free memoryは291,408 KiB、測定後温度は58.4°C、swap増加・OOM・thermal throttlingはなかった。出力は全12件がREADYとなり、合計55,056 bytes、必要な拡張子はすべて`.webp`だった。

## Leaseと整合性

- 並列2で最大2件、2個の異なるworker tokenを観測し、上限超過はなかった。
- 最大heartbeat ageは8.952秒で、10秒のheartbeat間隔より短かった。そのため今回の短い個別Jobでは定期更新回数は0だった。
- 「heartbeat期限超過かつ有効な`GENERATION` Leaseなし」は0件だった。claim直後から生成Lease取得前までの短い状態は、出力生成開始前のため違反に含めない。
- queuedは各run開始時に12件、runningは設定上限以下、最終的に12件すべてCOMPLETEDとなった。重複READYと部分出力公開はなかった。

## 上位並列数を採用しない理由

`CPUQuota=175%`で並列1・2・4・6・8を測定した。並列4は並列2より3.7%だけ短縮、並列6はCPU余力23.06%で基準外となった。並列8は初回35.677秒・CPU余力30.00%だったが、反復で39.273秒・CPU余力23.27%となり安定しなかった。高い並列数は個々の外部processを増やす一方、CPU quota内の競合を増やすため、並列2を採用した。

## 再実行

Raspberry Piへ対象releaseを配置したうえで、rootとして次を実行する。

```bash
KURASTORAGE_DEPLOY_CONFIG=/etc/kurastorage/deploy.env \
KURASTORAGE_BENCHMARK_OUTPUT=/tmp/kurastorage-thumbnail-benchmark-results \
KURASTORAGE_BENCHMARK_CONCURRENCIES='1 2 4' \
scripts/e2e/benchmark-thumbnail-concurrency.sh
```

スクリプトは`ks-20260906-thumbbench-`prefixの専用User、Folder、File、Jobのexact IDを`manifest.tsv`へ記録する。Folder以下はAPIのTrash/Purgeで消去し、専用Userは測定後に無効化する。
