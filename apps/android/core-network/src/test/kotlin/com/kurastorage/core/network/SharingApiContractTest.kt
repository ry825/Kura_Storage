package com.kurastorage.core.network

import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class SharingApiContractTest {
    private lateinit var server: MockWebServer
    private lateinit var api: KuraStorageApi

    @Before fun setUp() {
        server = MockWebServer().apply { start() }
        api = KuraStorageApi(server.url("/api/v1").toString().removeSuffix("/"), OkHttpClient())
    }

    @After fun tearDown() = server.shutdown()

    @Test
    fun `all sharing endpoints preserve method path query authorization and body`() =
        runTest {
            server.enqueue(json(CANDIDATES))
            server.enqueue(json(SHARE).setResponseCode(201))
            server.enqueue(json(PAGE))
            server.enqueue(json(SHARE))
            server.enqueue(json(SHARE))
            server.enqueue(MockResponse().setResponseCode(204))
            server.enqueue(MockResponse().setResponseCode(204))

            val candidates = api.listCandidates("token") as NetworkCallResult.Success
            assertEquals(USER, candidates.value.single().userId)
            assertRequest("GET", "/api/v1/shares/candidates")
            val created =
                api.createShare("token", CreateShareRequestDto(TARGET, listOf(CreateShareMemberDto(USER, "VIEWER")))) as
                    NetworkCallResult.Success
            assertEquals(SHARE_ID, created.value.id)
            val create = server.takeRequest()
            assertEquals("POST", create.method)
            assertEquals("/api/v1/shares", create.path)
            assertTrue(create.body.readUtf8().contains("\"permission\":\"VIEWER\""))
            val page = api.listShares("token", "received", "FOLDER", 2, 50) as NetworkCallResult.Success
            assertEquals(51, page.value.totalCount)
            assertRequest("GET", "/api/v1/shares?scope=received&targetType=FOLDER&page=2&pageSize=50")
            val detail = api.getShare("token", SHARE_ID) as NetworkCallResult.Success
            assertEquals("MANAGER", detail.value.permission)
            assertRequest("GET", "/api/v1/shares/$SHARE_ID")
            val updated =
                api.setMember("token", SHARE_ID, USER, SetShareMemberRequestDto("EDITOR")) as
                    NetworkCallResult.Success
            assertEquals(
                "VIEWER",
                updated.value.members
                    .single()
                    .permission,
            )
            val put = server.takeRequest()
            assertEquals("PUT", put.method)
            assertEquals("/api/v1/shares/$SHARE_ID/members/$USER", put.path)
            assertEquals("{\"permission\":\"EDITOR\"}", put.body.readUtf8())
            api.removeMember("token", SHARE_ID, USER)
            assertRequest("DELETE", "/api/v1/shares/$SHARE_ID/members/$USER")
            api.deleteShare("token", SHARE_ID)
            assertRequest("DELETE", "/api/v1/shares/$SHARE_ID")
        }

    @Test
    fun `401 is delegated and incomplete response fails safely`() =
        runTest {
            server.enqueue(MockResponse().setResponseCode(401))
            assertEquals(NetworkCallResult.Unauthorized, api.listCandidates("expired"))
            server.enqueue(json("{\"id\":\"$SHARE_ID\"}"))
            val failure = runCatching { api.getShare("token", SHARE_ID) }.exceptionOrNull()
            assertTrue(failure is KuraStorageException.Network)
        }

    @Test
    fun `sharing errors preserve every stable error code`() =
        runTest {
            listOf(
                ErrorCode.INVALID_SHARE_PERMISSION to 400,
                ErrorCode.SHARE_NOT_FOUND to 404,
                ErrorCode.SHARE_MEMBER_NOT_FOUND to 404,
                ErrorCode.SHARE_CONFLICT to 409,
                ErrorCode.SHARE_OPERATION_NOT_ALLOWED to 409,
            ).forEach { (code, status) ->
                server.enqueue(
                    json("""{"code":"$code","message":"failed","requestId":"share-request","details":{}}""")
                        .setResponseCode(status),
                )

                val failure =
                    runCatching { api.getShare("token", SHARE_ID) }.exceptionOrNull() as
                        KuraStorageException.Api
                assertEquals(code, failure.error.code)
                assertEquals("share-request", failure.error.requestId)
            }
        }

    private fun assertRequest(
        method: String,
        path: String,
    ) {
        val request = server.takeRequest()
        assertEquals(method, request.method)
        assertEquals(path, request.path)
        assertEquals("Bearer token", request.getHeader("Authorization"))
    }

    private fun json(body: String) = MockResponse().setHeader("Content-Type", "application/json").setBody(body)

    private companion object {
        const val SHARE_ID = "11111111-1111-1111-1111-111111111111"
        const val TARGET = "22222222-2222-2222-2222-222222222222"
        const val USER = "33333333-3333-3333-3333-333333333333"
        const val OWNER = "44444444-4444-4444-4444-444444444444"
        const val CANDIDATES = """[{"userId":"$USER","displayName":"Alex"}]"""
        val SHARE =
            """
            {
              "id":"$SHARE_ID","targetEntryId":"$TARGET","entryType":"FOLDER","name":"Photos",
              "owner":{"id":"$OWNER","displayName":"Owner"},"permission":"MANAGER",
              "members":[{"userId":"$USER","displayName":"Alex","permission":"VIEWER"}],
              "createdAt":"2026-08-23T00:00:00Z","updatedAt":"2026-08-23T00:00:00Z"
            }
            """.trimIndent()
        val PAGE = """{"items":[$SHARE],"page":2,"pageSize":50,"totalCount":51}"""
    }
}
