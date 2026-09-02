@file:Suppress("MaxLineLength")

package com.kurastorage.core.data

import com.kurastorage.core.model.ActivityDeleteKind
import com.kurastorage.core.model.ActivityDetail
import com.kurastorage.core.model.ActivityShareAction
import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserActivityType
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.ActivityApi
import com.kurastorage.core.network.ActivityItemDto
import com.kurastorage.core.network.ActivityPageDto
import com.kurastorage.core.network.NetworkCallResult
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class ActivityRepositoryTest {
    @Test
    fun `repository maps typed details and accessible target`() =
        runTest {
            val api = FakeActivityApi(page(item(type = "EDIT", version = 3, editKind = "TEXT_SAVE"), "next"))
            val repository = DefaultActivityRepository(api, executor())

            val result = repository.list(UserActivityType.EDIT, null, 50)

            assertEquals("next", result.nextCursor)
            assertEquals(ID, result.items.single().targetEntryId)
            assertEquals(ActivityDetail.Edit(3, com.kurastorage.core.model.ActivityEditKind.TEXT_SAVE), result.items.single().detail)
            assertEquals(listOf("EDIT"), api.types)
        }

    @Test
    fun `unknown type is fail closed and does not retain raw detail`() =
        runTest {
            val api = FakeActivityApi(page(item(type = "FUTURE", version = 9, editKind = "FUTURE_KIND"), null))
            val result = DefaultActivityRepository(api, executor()).list()

            assertEquals(UserActivityType.UNKNOWN, result.items.single().type)
            assertEquals(ActivityDetail.Unsupported, result.items.single().detail)
            assertNull(result.items.single().targetEntryId)
        }

    @Test
    fun `known type rejects mismatched detail contract`() =
        runTest {
            val api = FakeActivityApi(page(item(type = "MOVE", version = 3), null))

            assertTrue(
                runCatching { DefaultActivityRepository(api, executor()).list() }.exceptionOrNull() is
                    KuraStorageException.InvalidServerResponse,
            )
        }

    @Test
    fun `repository maps every known detail type`() =
        runTest {
            val cases =
                listOf(
                    item() to ActivityDetail.Upload(1),
                    item(type = "MOVE", version = null).copy(sourceParentName = "Before", destinationParentName = "After") to
                        ActivityDetail.Move("Before", "After"),
                    item(type = "EDIT", version = 2, editKind = "VERSION_RESTORE") to
                        ActivityDetail.Edit(2, com.kurastorage.core.model.ActivityEditKind.VERSION_RESTORE),
                    item(type = "SHARE", version = null).copy(
                        recipientDisplayName = "Blair",
                        sharePermission = "EDITOR",
                        shareAction = "UPDATED",
                    ) to ActivityDetail.Share("Blair", com.kurastorage.core.model.SharePermission.EDITOR, ActivityShareAction.UPDATED),
                    item(type = "DELETE", version = null).copy(deleteKind = "TRASHED") to
                        ActivityDetail.Delete(ActivityDeleteKind.TRASHED),
                )

            cases.forEach { (dto, expected) ->
                assertEquals(
                    expected,
                    DefaultActivityRepository(FakeActivityApi(page(dto, null)), executor())
                        .list()
                        .items
                        .single()
                        .detail,
                )
            }
        }

    @Test
    fun `repository refreshes once after unauthorized`() =
        runTest {
            val authentication = FakeAuthenticationRepository()
            val api = RetryActivityApi(page(item(), null))

            val result = DefaultActivityRepository(api, AuthenticatedRequestExecutor(authentication)).list()

            assertEquals(1, result.items.size)
            assertEquals(1, authentication.refreshCalls)
            assertEquals(listOf("access", "refreshed"), api.tokens)
        }

    @Test
    fun `pager rejects repeated cursors`() =
        runTest {
            val first = page(item(), "cursor")
            val api = SequencedActivityApi(mutableListOf(first, first))
            val pager = ActivityPager(DefaultActivityRepository(api, executor()))
            pager.refresh()

            assertTrue(runCatching { pager.loadNext() }.exceptionOrNull() is KuraStorageException.InvalidServerResponse)
        }

    private fun executor() = AuthenticatedRequestExecutor(FakeAuthenticationRepository())

    private class FakeAuthenticationRepository : AuthenticationRepository {
        private var token = "access"
        var refreshCalls = 0

        private fun session() = AuthSession(DeviceId("device"), token, "refresh", Instant.MAX, Instant.MAX, UserRole.MEMBER)

        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ) = session()

        override suspend fun login(
            username: String,
            password: String,
        ) = session()

        override suspend fun refresh() = session()

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String): AuthSession {
            refreshCalls++
            token = "refreshed"
            return session()
        }

        override suspend fun logout() = Unit

        override fun accessToken(): String = token
    }

    private class FakeActivityApi(
        private val response: ActivityPageDto,
    ) : ActivityApi {
        val types = mutableListOf<String?>()

        override suspend fun listActivities(
            accessToken: String,
            type: String?,
            cursor: String?,
            pageSize: Int,
        ): NetworkCallResult<ActivityPageDto> {
            types += type
            return NetworkCallResult.Success(response)
        }
    }

    private class SequencedActivityApi(
        private val responses: MutableList<ActivityPageDto>,
    ) : ActivityApi {
        override suspend fun listActivities(
            accessToken: String,
            type: String?,
            cursor: String?,
            pageSize: Int,
        ) = NetworkCallResult.Success(responses.removeAt(0))
    }

    private class RetryActivityApi(
        private val response: ActivityPageDto,
    ) : ActivityApi {
        val tokens = mutableListOf<String>()

        override suspend fun listActivities(
            accessToken: String,
            type: String?,
            cursor: String?,
            pageSize: Int,
        ): NetworkCallResult<ActivityPageDto> {
            tokens += accessToken
            return if (tokens.size == 1) NetworkCallResult.Unauthorized else NetworkCallResult.Success(response)
        }
    }

    private companion object {
        const val ID = "00000000-0000-4000-8000-000000000001"

        fun item(
            type: String = "UPLOAD",
            version: Long? = 1,
            editKind: String? = null,
        ) = ActivityItemDto(
            type = type,
            occurredAt = "2026-09-02T01:02:03Z",
            actorDisplayName = "Alex",
            actorDeviceName = "Phone",
            targetEntryId = ID,
            targetType = "FILE",
            targetName = "notes.txt",
            ownerDisplayName = "Alex",
            resultingFileVersion = version,
            editKind = editKind,
        )

        fun page(
            item: ActivityItemDto,
            cursor: String?,
        ) = ActivityPageDto(listOf(item), cursor)
    }
}
