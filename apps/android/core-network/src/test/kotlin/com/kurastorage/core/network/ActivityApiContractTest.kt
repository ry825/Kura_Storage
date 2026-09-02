@file:Suppress("MaxLineLength")

package com.kurastorage.core.network

import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class ActivityApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: KuraStorageApi

    @Before fun setUp() {
        server = MockWebServer().apply { start() }
        api = KuraStorageApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient())
    }

    @After fun tearDown() = server.shutdown()

    @Test fun `activity endpoint preserves auth filter opaque cursor and page contract`() =
        runTest {
            server.enqueue(json(PAGE))

            val result = api.listActivities("token", "EDIT", "opaque-cursor", 50) as NetworkCallResult.Success

            assertEquals("next", result.value.nextCursor)
            assertEquals(
                "TEXT_SAVE",
                result.value.items
                    .single()
                    .editKind,
            )
            val request = server.takeRequest()
            assertEquals("GET", request.method)
            assertEquals("/api/v1/activities?type=EDIT&cursor=opaque-cursor&pageSize=50", request.path)
            assertEquals("Bearer token", request.getHeader("Authorization"))
        }

    @Test fun `activity endpoint delegates unauthorized and rejects incomplete response`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            assertEquals(NetworkCallResult.Unauthorized, api.listActivities("expired", null, null, 50))

            server.enqueue(json("{\"items\":[{\"type\":\"EDIT\"}]}"))
            assertTrue(runCatching { api.listActivities("token", null, null, 50) }.exceptionOrNull() != null)
        }

    private fun json(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private companion object {
        val PAGE =
            """
            {
              "items": [{
                "type": "EDIT", "occurredAt": "2026-09-02T01:02:03Z",
                "actorDisplayName": "Alex", "actorDeviceName": "Phone",
                "targetEntryId": "00000000-0000-4000-8000-000000000001",
                "targetType": "FILE", "targetName": "notes.txt", "ownerDisplayName": "Alex",
                "sourceParentName": null, "destinationParentName": null,
                "resultingFileVersion": 2, "editKind": "TEXT_SAVE",
                "recipientDisplayName": null, "sharePermission": null,
                "shareAction": null, "deleteKind": null
              }],
              "nextCursor": "next"
            }
            """.trimIndent()
    }
}
