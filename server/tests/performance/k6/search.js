import http from "k6/http";
import { check, sleep } from "k6";
import { Rate } from "k6/metrics";

const baseUrl = (__ENV.SEARCH_BASE_URL || "").replace(/\/$/, "");
const tokens = (__ENV.SEARCH_TOKENS || "").split(",").filter(Boolean);
const failures = new Rate("search_failures");

if (!baseUrl || tokens.length !== 10) {
  throw new Error("SEARCH_BASE_URL and exactly 10 comma-separated SEARCH_TOKENS are required.");
}

const cases = [
  "q=performance-file-100&pageSize=50",
  "q=per&pageSize=50",
  "q=pe&pageSize=50",
  "entryType=FILE&pageSize=100",
  "entryType=FOLDER&pageSize=100",
  "fileCategory=IMAGE&pageSize=100",
  "fileCategory=VIDEO&pageSize=100",
  "fileCategory=AUDIO&pageSize=100",
  "fileCategory=DOCUMENT&pageSize=100",
  "fileCategory=ARCHIVE&pageSize=100",
  "fileCategory=OTHER&pageSize=100",
  "status=ACTIVE&pageSize=100",
  "status=MISSING_CANDIDATE&pageSize=100",
  "status=MISSING&pageSize=100",
  "minSize=1048576&maxSize=1048676&pageSize=100",
  "updatedFrom=2026-01-01T00%3A00%3A00Z&pageSize=100",
  "q=file-20&fileCategory=DOCUMENT&pageSize=100",
  "q=performance&status=ACTIVE&page=10&pageSize=100",
  "ownerUserId=c4c6fcaa-4113-9611-e8af-0c5b710871a4&pageSize=100",
  "shareTargetId=be58d0b2-c159-1b3f-d317-9dfe81f381ca&pageSize=100",
];

export const options = {
  vus: Number(__ENV.SEARCH_VUS || 4),
  duration: __ENV.SEARCH_DURATION || "5m",
  thresholds: {
    http_req_duration: ["p(95)<2000"],
    search_failures: ["rate<0.01"],
  },
};

export function setup() {
  // Warm every representative plan without printing the URL, query, or token.
  for (let index = 0; index < cases.length; index += 1) {
    http.get(`${baseUrl}/api/v1/search?${cases[index]}`, {
      headers: { Authorization: `Bearer ${tokens[index % tokens.length]}` },
      tags: { name: "GET /api/v1/search", phase: "warmup" },
    });
  }
}

export default function () {
  const index = (__VU + __ITER) % cases.length;
  const response = http.get(`${baseUrl}/api/v1/search?${cases[index]}`, {
    headers: { Authorization: `Bearer ${tokens[index % tokens.length]}` },
    tags: { name: "GET /api/v1/search", case: `case-${index + 1}` },
  });
  const ok = check(response, {
    "search returns 200": (value) => value.status === 200,
    "search returns bounded page": (value) => {
      try {
        const body = value.json();
        return Array.isArray(body.items) && body.items.length <= 100;
      } catch (_) {
        return false;
      }
    },
  });
  failures.add(!ok);
  sleep(0.2);
}
