#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
    printf 'Run this benchmark as root on the Raspberry Pi.\n' >&2
    exit 1
fi

config_file="${KURASTORAGE_DEPLOY_CONFIG:?Set KURASTORAGE_DEPLOY_CONFIG.}"
output_directory="${KURASTORAGE_BENCHMARK_OUTPUT:?Set KURASTORAGE_BENCHMARK_OUTPUT.}"
# shellcheck disable=SC1090
source "${config_file}"

run_id="ks-20260906-thumbbench-$(date -u +%Y%m%dT%H%M%SZ)"
username="${run_id}"
work_directory="$(mktemp -d /tmp/kurastorage-thumbnail-benchmark.XXXXXX)"
manifest="${output_directory}/manifest.tsv"
results="${output_directory}/results.tsv"
mkdir -p "${output_directory}"
chmod 0700 "${output_directory}"
printf '# run_id\t%s\n# type\texact_id\tlabel\n' "${run_id}" >"${manifest}"
printf 'concurrency\tjobs\tmax_running\tmax_tokens\tmax_heartbeat_age_ms\theartbeat_updates\tlease_violations\tfirst_ready_ms\ttotal_ms\tsucceeded\tfailed\tbytes\tlist_p95_ms\trange_p95_ms\tidle_avg_pct\tidle_min_pct\tiowait_avg_pct\tiowait_max_pct\tswap_in_blocks\tswap_out_blocks\tmin_free_kib\ttemp_before_c\ttemp_after_c\tthrottled_before\tthrottled_after\n' >"${results}"

curl_common=(
    curl --silent --show-error --retry 3 --retry-all-errors --retry-delay 1
    --cacert "${KURASTORAGE_TLS_CA_CERT_FILE}"
    --resolve "${KURASTORAGE_API_HOSTNAME}:443:${KURASTORAGE_LAN_API_IP}"
)
base_url="https://${KURASTORAGE_API_HOSTNAME}/api/v1"
generated_credential="$(openssl rand -base64 30 | tr -d '\n')Aa1!"
access_token=""
concurrencies="${KURASTORAGE_BENCHMARK_CONCURRENCIES:-1 2 4}"
declare -a cleanup_ids=()
trap 'printf "BENCHMARK_ERROR_LINE=%s\n" "${LINENO}" >&2' ERR

for configured_concurrency in ${concurrencies}; do
    [[ "${configured_concurrency}" =~ ^[1-8]$ ]] || {
        printf 'Invalid benchmark concurrency: %s\n' "${configured_concurrency}" >&2
        exit 1
    }
done

request() {
    local method="$1" path="$2" expected="$3" body_file="$4" data="${5:-}"
    local -a arguments=(
        --output "${body_file}" --write-out '%{http_code}' --request "${method}"
        --header "Authorization: Bearer ${access_token}"
    )
    if [[ "${method}" == DELETE && "${path}" == /trash/* ]]; then
        arguments+=(--header "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)")
    fi
    if [[ -n "${data}" ]]; then
        arguments+=(--header 'Content-Type: application/json' --data "${data}")
    elif [[ "${method}" == POST ]]; then
        arguments+=(--data '')
    fi
    local status
    status="$("${curl_common[@]}" "${arguments[@]}" "${base_url}${path}")"
    if [[ ",${expected}," != *",${status},"* ]]; then
        printf 'Request failed: %s %s expected=%s actual=%s\n' "${method}" "${path}" "${expected}" "${status}" >&2
        jq -c '{code,message}' "${body_file}" 2>/dev/null || true
        return 1
    fi
}

cleanup() {
    local exit_code=$?
    set +e
    systemctl stop kurastorage-worker.service
    local file_id
    for file_id in "${cleanup_ids[@]}"; do
        request DELETE "/files/${file_id}" '200,404' "${work_directory}/cleanup-trash.json"
        request DELETE "/trash/${file_id}" '204,404' "${work_directory}/cleanup-purge.json"
    done
    if [[ -n "${benchmark_user_id:-}" ]]; then
        [[ "${benchmark_user_id}" =~ ^[0-9a-f-]{36}$ ]] || exit 1
        runuser -u postgres -- psql --no-psqlrc --dbname "${KURASTORAGE_POSTGRES_DATABASE}" \
            --set ON_ERROR_STOP=1 --command \
            "UPDATE users SET status='DISABLED', updated_at=CURRENT_TIMESTAMP WHERE id='${benchmark_user_id}'::uuid AND username_normalized=upper('${username}');" \
            >/dev/null
    fi
    systemctl start kurastorage-worker.service
    [[ "${work_directory}" == /tmp/kurastorage-thumbnail-benchmark.* ]] || exit 1
    rm -rf -- "${work_directory}"
    exit "${exit_code}"
}
trap cleanup EXIT

printf '%s\n' "${generated_credential}" | runuser -u kurastorage-api -- env \
    DOTNET_ENVIRONMENT=Production KURASTORAGE_SECRETS_DIR=/etc/kurastorage/secrets \
    bash -c "cd /opt/kurastorage/current && ./KuraStorage.AdminCli user create '${username}' '${username}' MEMBER --password-stdin" \
    >/dev/null

registration_body="$(jq -nc --arg u "${username}" --arg p "${generated_credential}" \
    '{username:$u,password:$p,deviceName:"thumbnail-benchmark"}')"
registration_status="$("${curl_common[@]}" --output "${work_directory}/registration.json" \
    --write-out '%{http_code}' --header 'Content-Type: application/json' \
    --data "${registration_body}" "${base_url}/auth/register-device")"
[[ "${registration_status}" == 200 ]]
access_token="$(jq -er '.accessToken' "${work_directory}/registration.json")"
benchmark_user_id="$(jq -er '.userId' "${work_directory}/registration.json")"
printf 'user\t%s\t%s\n' "${benchmark_user_id}" "${username}" >>"${manifest}"

request GET /files 200 "${work_directory}/root.json"
root_id="$(jq -er '.parentId' "${work_directory}/root.json")"

ffmpeg -nostdin -v error -f lavfi -i 'testsrc2=size=4000x3000:rate=1' \
    -frames:v 1 -q:v 2 "${work_directory}/photo.jpg"
ffmpeg -nostdin -v error -f lavfi -i 'testsrc2=size=1920x1080:rate=30' \
    -f lavfi -i 'sine=frequency=700:sample_rate=48000' -t 6 \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac "${work_directory}/video.mp4"
printf '%s\n' \
    '%!PS-Adobe-3.0' \
    '<< /PageSize [1800 2400] >> setpagedevice' \
    '/Helvetica findfont 64 scalefont setfont' \
    '100 2100 moveto (KuraStorage thumbnail concurrency benchmark) show' \
    '0.1 0.4 0.8 setrgbcolor 100 100 1500 1700 rectfill' \
    'showpage' >"${work_directory}/fixture.ps"
ps2pdf "${work_directory}/fixture.ps" "${work_directory}/fixture.pdf"

upload_file() {
    local source_file="$1" mime_type="$2" file_name="$3" destination_id="$4" output_file="$5"
    local size status
    size="$(stat -c %s "${source_file}")"
    status="$("${curl_common[@]}" --output "${output_file}" --write-out '%{http_code}' \
        --header "Authorization: Bearer ${access_token}" \
        --header "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" \
        --form "destinationFolderId=${destination_id}" --form "fileName=${file_name}" \
        --form "size=${size}" --form "file=@${source_file};type=${mime_type}" \
        "${base_url}/files/upload")"
    [[ "${status}" == 200 ]]
    jq -er '.id' "${output_file}"
}

percentile95_ms() {
    sort -n "$1" | awk '{ values[NR]=$1 } END { rank=int((NR*95+99)/100); printf "%.3f", values[rank]*1000 }'
}

temperature() {
    if command -v vcgencmd >/dev/null 2>&1; then
        vcgencmd measure_temp | sed -E "s/temp=([0-9.]+).*/\1/"
    else
        awk '{printf "%.1f", $1/1000}' /sys/class/thermal/thermal_zone0/temp
    fi
}

throttled() {
    if command -v vcgencmd >/dev/null 2>&1; then
        vcgencmd get_throttled | cut -d= -f2
    else
        printf 'unavailable'
    fi
}

run_foreground_load() {
    local run="$1" video_id="$2" folder_id="$3"
    local list_times="${work_directory}/list-${run}.txt"
    local range_times="${work_directory}/range-${run}.txt"
    : >"${list_times}"
    : >"${range_times}"
    for _ in $(seq 1 80); do
        "${curl_common[@]}" --output /dev/null --write-out '%{time_total}\n' \
            --header "Authorization: Bearer ${access_token}" \
            "${base_url}/files?parentId=${folder_id}&pageSize=100" >>"${list_times}"
        sleep 0.5
    done &
    local list_pid=$!
    for _ in $(seq 1 40); do
        "${curl_common[@]}" --output /dev/null --write-out '%{time_total}\n' \
            --header "Authorization: Bearer ${access_token}" --header 'Range: bytes=0-262143' \
            "${base_url}/files/${video_id}/content?variant=original&disposition=inline" >>"${range_times}"
        sleep 0.15
    done &
    local range_pid=$!

    upload_file "${work_directory}/photo.jpg" image/jpeg \
        "${run_id}-manual-${run}.jpg" "${folder_id}" "${work_directory}/manual-${run}.json" \
        >"${work_directory}/manual-${run}.id" &
    local manual_pid=$!

    (
        local key checksum size session_id chunk_status complete_status compare_status decision
        key="$(cat /proc/sys/kernel/random/uuid)"
        checksum="$(sha256sum "${work_directory}/photo.jpg" | cut -d' ' -f1)"
        size="$(stat -c %s "${work_directory}/photo.jpg")"
        local compare_body session_body
        compare_body="$(jq -nc --arg folder "${folder_id}" --arg key "${key}" \
            --arg path "benchmark/${run}/backup.jpg" --arg checksum "${checksum}" \
            --argjson size "${size}" \
            '{destinationFolderId:$folder,items:[{localDocumentKey:$key,relativePath:$path,size:$size,modifiedAt:"2026-09-06T00:00:00Z",checksum:$checksum}]}')"
        compare_status="$("${curl_common[@]}" --output "${work_directory}/compare-${run}.json" --write-out '%{http_code}' \
            --header "Authorization: Bearer ${access_token}" --header 'Content-Type: application/json' \
            --data "${compare_body}" "${base_url}/backup/compare")"
        [[ "${compare_status}" == 200 ]] || {
            printf 'BACKUP_COMPARE_STATUS=%s\n' "${compare_status}" >&2
            return 1
        }
        decision="$(jq -er '.items[0].decision' "${work_directory}/compare-${run}.json")"
        [[ "${decision}" == NEW ]]
        session_body="$(jq -nc --arg folder "${folder_id}" --arg key "${key}" \
            --arg name "${run_id}-backup-${run}.jpg" --arg path "benchmark/${run}/backup.jpg" \
            --arg checksum "${checksum}" --argjson size "${size}" \
            '{destinationFolderId:$folder,fileName:$name,size:$size,contentType:"image/jpeg",sha256:$checksum,backup:{localDocumentKey:$key,relativePath:$path,modifiedAt:"2026-09-06T00:00:00Z",decision:"NEW",expectedRemoteFileId:null,expectedRemoteFileVersion:null}}')"
        session_status="$("${curl_common[@]}" --output "${work_directory}/session-${run}.json" --write-out '%{http_code}' \
            --header "Authorization: Bearer ${access_token}" --header 'Content-Type: application/json' \
            --header "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" --data "${session_body}" \
            "${base_url}/upload-sessions")"
        [[ "${session_status}" == 201 ]] || {
            printf 'BACKUP_SESSION_STATUS=%s\n' "${session_status}" >&2
            return 1
        }
        session_id="$(jq -er '.id' "${work_directory}/session-${run}.json")"
        chunk_status="$("${curl_common[@]}" --output /dev/null --write-out '%{http_code}' --request PUT \
            --header "Authorization: Bearer ${access_token}" --header 'Content-Type: application/octet-stream' \
            --header 'Upload-Offset: 0' --header "X-Chunk-Sha256: ${checksum}" \
            --data-binary "@${work_directory}/photo.jpg" "${base_url}/upload-sessions/${session_id}/chunks")"
        [[ "${chunk_status}" == 200 ]] || {
            printf 'BACKUP_CHUNK_STATUS=%s\n' "${chunk_status}" >&2
            return 1
        }
        complete_status="$("${curl_common[@]}" --output "${work_directory}/backup-${run}.json" --write-out '%{http_code}' \
            --request POST --header "Authorization: Bearer ${access_token}" --header 'Content-Length: 0' \
            "${base_url}/upload-sessions/${session_id}/complete")"
        [[ "${complete_status}" == 200 ]] || {
            printf 'BACKUP_COMPLETE_STATUS=%s\n' "${complete_status}" >&2
            return 1
        }
        jq -er '.id' "${work_directory}/backup-${run}.json" >"${work_directory}/backup-${run}.id"
    ) &
    local backup_pid=$!
    local foreground_failed=0
    wait "${list_pid}" || {
        printf 'FOREGROUND_LIST_FAILED=1\n' >&2
        foreground_failed=1
    }
    wait "${range_pid}" || {
        printf 'FOREGROUND_RANGE_FAILED=1\n' >&2
        foreground_failed=1
    }
    wait "${manual_pid}" || {
        printf 'FOREGROUND_MANUAL_UPLOAD_FAILED=1\n' >&2
        foreground_failed=1
    }
    wait "${backup_pid}" || {
        printf 'FOREGROUND_BACKUP_UPLOAD_FAILED=1\n' >&2
        foreground_failed=1
    }
    [[ "${foreground_failed}" == 0 ]]
}

for concurrency in ${concurrencies}; do
    systemctl stop kurastorage-worker.service
    temporary_config="${work_directory}/appsettings.${concurrency}.json"
    jq --argjson concurrency "${concurrency}" \
        '.Media.MaximumConcurrentThumbnailJobs=$concurrency' \
        /opt/kurastorage/current/appsettings.Production.json >"${temporary_config}"
    install -m 0640 -o root -g "${KURASTORAGE_STORAGE_ACCESS_GROUP}" \
        "${temporary_config}" /opt/kurastorage/current/appsettings.Production.json

    request POST /folders 200 "${work_directory}/folder-${concurrency}.json" \
        "$(jq -nc --arg p "${root_id}" --arg n "${run_id}-c${concurrency}" '{parentId:$p,name:$n}')"
    folder_id="$(jq -er '.id' "${work_directory}/folder-${concurrency}.json")"
    cleanup_ids+=("${folder_id}")
    printf 'folder\t%s\tc%s\n' "${folder_id}" "${concurrency}" >>"${manifest}"

    file_ids=()
    video_id=""
    for kind in photo video pdf; do
        case "${kind}" in
            photo) source_file="${work_directory}/photo.jpg"; mime_type=image/jpeg; extension=jpg ;;
            video) source_file="${work_directory}/video.mp4"; mime_type=video/mp4; extension=mp4 ;;
            pdf) source_file="${work_directory}/fixture.pdf"; mime_type=application/pdf; extension=pdf ;;
        esac
        for item in 1 2 3 4; do
            name="${run_id}-c${concurrency}-${kind}-${item}.${extension}"
            file_id="$(upload_file "${source_file}" "${mime_type}" "${name}" "${folder_id}" \
                "${work_directory}/upload-${concurrency}-${kind}-${item}.json")"
            file_ids+=("${file_id}")
            [[ "${kind}" == video && -z "${video_id}" ]] && video_id="${file_id}"
            printf 'file\t%s\t%s\n' "${file_id}" "${name}" >>"${manifest}"
        done
    done

    sql_ids="$(printf "'%s'," "${file_ids[@]}")"
    sql_ids="${sql_ids%,}"
    for file_id in "${file_ids[@]}"; do
        status="$("${curl_common[@]}" --output "${work_directory}/enqueue.json" --write-out '%{http_code}' \
            --header "Authorization: Bearer ${access_token}" \
            "${base_url}/files/${file_id}/content?variant=thumbnail&disposition=inline")"
        [[ "${status}" == 202 ]]
        printf 'job\t%s\tc%s\n' "$(jq -er '.jobId' "${work_directory}/enqueue.json")" "${concurrency}" >>"${manifest}"
    done

    queued="$(runuser -u postgres -- psql -At --no-psqlrc --dbname "${KURASTORAGE_POSTGRES_DATABASE}" \
        --command "SELECT count(*) FROM media_jobs j JOIN file_derivatives d ON d.id=j.derivative_id WHERE d.source_file_id IN (${sql_ids}) AND j.status='QUEUED';")"
    [[ "${queued}" == 12 ]]

    vmstat_log="${work_directory}/vmstat-${concurrency}.txt"
    vmstat 1 >"${vmstat_log}" &
    vmstat_pid=$!
    temp_before="$(temperature)"
    throttled_before="$(throttled)"
    start_ns="$(date +%s%N)"
    run_foreground_load "${concurrency}" "${video_id}" "${folder_id}" &
    foreground_pid=$!
    systemctl start kurastorage-worker.service

    max_running=0
    max_tokens=0
    max_heartbeat_age_ms=0
    heartbeat_updates=0
    lease_violations=0
    first_ready_ms=-1
    for _ in $(seq 1 2400); do
        read -r running ready failed tokens heartbeat_age updated_heartbeats invalid_leases <<<"$(runuser -u postgres -- psql -At -F ' ' --no-psqlrc \
            --dbname "${KURASTORAGE_POSTGRES_DATABASE}" --command \
            "SELECT count(*) FILTER (WHERE j.status='RUNNING'), count(*) FILTER (WHERE d.status='READY'), count(*) FILTER (WHERE j.status='FAILED'), count(DISTINCT j.worker_token) FILTER (WHERE j.status='RUNNING'), coalesce(round(extract(epoch FROM (CURRENT_TIMESTAMP-min(j.heartbeat_at) FILTER (WHERE j.status='RUNNING')))*1000),0), count(*) FILTER (WHERE j.status='RUNNING' AND j.heartbeat_at > j.started_at + interval '1 second'), count(*) FILTER (WHERE j.status='RUNNING' AND j.started_at < CURRENT_TIMESTAMP - interval '1 second' AND (j.heartbeat_at IS NULL OR j.heartbeat_at < CURRENT_TIMESTAMP - interval '15 seconds') AND NOT EXISTS (SELECT 1 FROM derivative_leases l WHERE l.derivative_id=d.id AND l.lease_type='GENERATION' AND l.expires_at>CURRENT_TIMESTAMP)) FROM media_jobs j JOIN file_derivatives d ON d.id=j.derivative_id WHERE d.source_file_id IN (${sql_ids});")"
        (( running > max_running )) && max_running="${running}"
        (( tokens > max_tokens )) && max_tokens="${tokens}"
        (( heartbeat_age > max_heartbeat_age_ms )) && max_heartbeat_age_ms="${heartbeat_age}"
        (( updated_heartbeats > heartbeat_updates )) && heartbeat_updates="${updated_heartbeats}"
        (( invalid_leases > lease_violations )) && lease_violations="${invalid_leases}"
        now_ns="$(date +%s%N)"
        if [[ "${ready}" -gt 0 && "${first_ready_ms}" -lt 0 ]]; then
            first_ready_ms="$(((now_ns - start_ns) / 1000000))"
        fi
        [[ "${failed}" == 0 ]]
        if [[ "${ready}" == 12 ]]; then
            total_ms="$(((now_ns - start_ns) / 1000000))"
            break
        fi
        sleep 0.05
    done
    [[ "${ready}" == 12 ]]
    wait "${foreground_pid}"
    kill "${vmstat_pid}" 2>/dev/null || true
    wait "${vmstat_pid}" 2>/dev/null || true
    temp_after="$(temperature)"
    throttled_after="$(throttled)"

    manual_id="$(cat "${work_directory}/manual-${concurrency}.id")"
    backup_id="$(cat "${work_directory}/backup-${concurrency}.id")"
    printf 'foreground-file\t%s\tmanual-c%s\n' "${manual_id}" "${concurrency}" >>"${manifest}"
    printf 'foreground-file\t%s\tbackup-c%s\n' "${backup_id}" "${concurrency}" >>"${manifest}"

    read -r succeeded failed bytes invalid_formats <<<"$(runuser -u postgres -- psql -At -F ' ' --no-psqlrc \
        --dbname "${KURASTORAGE_POSTGRES_DATABASE}" --command \
        "SELECT count(*) FILTER (WHERE j.status='COMPLETED'), count(*) FILTER (WHERE j.status='FAILED'), coalesce(sum(d.size),0), count(*) FILTER (WHERE d.status='READY' AND d.relative_path NOT LIKE '%.webp') FROM media_jobs j JOIN file_derivatives d ON d.id=j.derivative_id WHERE d.source_file_id IN (${sql_ids});")"
    [[ "${succeeded}" == 12 && "${failed}" == 0 && "${invalid_formats}" == 0 && "${bytes}" -gt 0 ]]
    runuser -u postgres -- psql -At --no-psqlrc --dbname "${KURASTORAGE_POSTGRES_DATABASE}" \
        --command "SELECT d.relative_path FROM file_derivatives d WHERE d.source_file_id IN (${sql_ids}) AND d.status='READY' ORDER BY d.id;" \
        >"${work_directory}/derivative-paths-${concurrency}.txt"
    [[ "$(wc -l <"${work_directory}/derivative-paths-${concurrency}.txt")" == 12 ]]
    while IFS= read -r relative_path; do
        [[ "${relative_path}" == "${KURASTORAGE_MEDIA_DERIVATIVE_ROOT}"/* && "${relative_path}" != *..* ]]
        [[ "$(file --brief --mime-type "${KURASTORAGE_STORAGE_ROOT}/${relative_path}")" == image/webp ]]
    done <"${work_directory}/derivative-paths-${concurrency}.txt"

    read -r idle_avg idle_min iowait_avg iowait_max swap_in swap_out min_free <<<"$(awk \
        'NR>2 && NF>=17 {n++; idle+=$15; if(n==1 || $15<idle_min) idle_min=$15; wait+=$16; if(n==1 || $16>wait_max) wait_max=$16; si+=$7; so+=$8; if(n==1 || $4<free_min) free_min=$4} END {printf "%.2f %.2f %.2f %.2f %d %d %d", idle/n,idle_min,wait/n,wait_max,si,so,free_min}' \
        "${vmstat_log}")"
    list_p95="$(percentile95_ms "${work_directory}/list-${concurrency}.txt")"
    range_p95="$(percentile95_ms "${work_directory}/range-${concurrency}.txt")"

    printf '%s\t12\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "${concurrency}" "${max_running}" "${max_tokens}" "${max_heartbeat_age_ms}" \
        "${heartbeat_updates}" "${lease_violations}" "${first_ready_ms}" "${total_ms}" \
        "${succeeded}" "${failed}" "${bytes}" "${list_p95}" "${range_p95}" \
        "${idle_avg}" "${idle_min}" "${iowait_avg}" "${iowait_max}" "${swap_in}" "${swap_out}" \
        "${min_free}" "${temp_before}" "${temp_after}" "${throttled_before}" "${throttled_after}" \
        >>"${results}"
done

printf 'THUMBNAIL_BENCHMARK=passed output=%s\n' "${output_directory}"
