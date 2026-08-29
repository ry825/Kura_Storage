package com.kurastorage.core.data

import com.kurastorage.core.model.AuthSession
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceId
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.StoredCredential
import com.kurastorage.core.model.UserRole
import com.kurastorage.core.network.EntryOrganizationStateDto
import com.kurastorage.core.network.FavoriteItemDto
import com.kurastorage.core.network.FavoritePageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.OrganizationApi
import com.kurastorage.core.network.OwnerSummaryDto
import com.kurastorage.core.network.TagItemDto
import com.kurastorage.core.network.TagNameRequestDto
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.time.Instant

class OrganizationRepositoryTest {
    @Test fun `favorite and tag mutations reconcile authoritative state and retry 401`() =
        runTest {
            val api = FakeApi().apply { unauthorizedOnce = true }
            val repository = DefaultOrganizationRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            assertTrue(repository.setFavorite(ENTRY, true).isFavorite)
            assertEquals(listOf("token", "refreshed"), api.mutationTokens)
            api.networkFailure = true
            assertEquals(listOf(TAG_ID), repository.setTag(ENTRY, TAG_ID, true).tags.map { it.id })
            assertEquals(2, api.stateCalls)
        }

    @Test fun `strict mapping rejects unknown metadata duplicate tags and invalid page`() =
        runTest {
            val api = FakeApi()
            val repository = DefaultOrganizationRepository(api, AuthenticatedRequestExecutor(FakeAuth()))
            assertEquals(
                ENTRY,
                repository
                    .listFavorites()
                    .items
                    .single()
                    .id,
            )
            api.favorite = api.favorite.copy(status = "FUTURE")
            assertInvalid { repository.listFavorites() }
            api.favorite = favorite()
            api.tags = listOf(tag(), tag())
            assertInvalid { repository.listTags() }
            api.tags = listOf(tag().copy(name = " bad\u0000"))
            assertInvalid { repository.listTags() }
            api.tags = (1..200).map(::tag)
            assertEquals(200, repository.listTags().size)
            api.tags = (1..201).map(::tag)
            assertInvalid { repository.listTags() }
            api.stateTags = (1..20).map(::tag)
            assertEquals(20, repository.state(ENTRY).tags.size)
            api.stateTags = (1..21).map(::tag)
            assertInvalid { repository.state(ENTRY) }
            api.tags = listOf(tag())
            api.total = -1
            assertInvalid { repository.listFavorites() }
        }

    @Test fun `favorite pager is bounded and rejects duplicate pages`() =
        runTest {
            val api = FakeApi().apply { total = 2 }
            val pager = FavoritePager(DefaultOrganizationRepository(api, AuthenticatedRequestExecutor(FakeAuth())), 1)
            pager.refresh()
            api.duplicateSecondPage = false
            assertEquals(listOf(ENTRY, ENTRY_2), pager.loadNext().items.map { it.id })
            val duplicate =
                FavoritePager(
                    DefaultOrganizationRepository(
                        api.apply { duplicateSecondPage = true },
                        AuthenticatedRequestExecutor(FakeAuth()),
                    ),
                    1,
                )
            duplicate.refresh()
            assertInvalid { duplicate.loadNext() }
        }

    private suspend fun assertInvalid(block: suspend () -> Unit) {
        assertTrue(runCatching { block() }.exceptionOrNull() is KuraStorageException.InvalidServerResponse)
    }

    private class FakeApi : OrganizationApi {
        var favorite = favorite()
        var tags = listOf(tag())
        var stateTags = listOf(tag())
        var total = 1
        var duplicateSecondPage = false
        var unauthorizedOnce = false
        var networkFailure = false
        val mutationTokens = mutableListOf<String>()
        var stateCalls = 0

        override suspend fun listFavorites(
            accessToken: String,
            page: Int,
            pageSize: Int,
        ) = NetworkCallResult.Success(
            FavoritePageDto(
                listOf(favorite.copy(id = if (page == 2 && !duplicateSecondPage) ENTRY_2 else ENTRY)),
                page,
                pageSize,
                total,
            ),
        )

        override suspend fun addFavorite(
            accessToken: String,
            entryId: String,
        ): NetworkCallResult<Unit> {
            mutationTokens += accessToken
            if (unauthorizedOnce && mutationTokens.size == 1) return NetworkCallResult.Unauthorized
            if (networkFailure) throw KuraStorageException.Network(IOException("unknown"))
            return NetworkCallResult.Success(Unit)
        }

        override suspend fun removeFavorite(
            accessToken: String,
            entryId: String,
        ) = addFavorite(accessToken, entryId)

        override suspend fun listTags(accessToken: String) = NetworkCallResult.Success(tags)

        override suspend fun createTag(
            accessToken: String,
            request: TagNameRequestDto,
        ) = NetworkCallResult.Success(tag().copy(name = request.name))

        override suspend fun renameTag(
            accessToken: String,
            tagId: String,
            request: TagNameRequestDto,
        ) = NetworkCallResult.Success(tag().copy(name = request.name))

        override suspend fun deleteTag(
            accessToken: String,
            tagId: String,
        ) = NetworkCallResult.Success(Unit)

        override suspend fun getEntryOrganization(
            accessToken: String,
            entryId: String,
        ): NetworkCallResult<EntryOrganizationStateDto> {
            stateCalls++
            return NetworkCallResult.Success(EntryOrganizationStateDto(true, stateTags))
        }

        override suspend fun attachTag(
            accessToken: String,
            entryId: String,
            tagId: String,
        ) = addFavorite(accessToken, entryId)

        override suspend fun detachTag(
            accessToken: String,
            entryId: String,
            tagId: String,
        ) = addFavorite(accessToken, entryId)
    }

    private class FakeAuth : AuthenticationRepository {
        override suspend fun storedCredential(): StoredCredential? = null

        override suspend fun register(
            route: ConnectionRoute,
            username: String,
            password: String,
            deviceName: String,
        ): AuthSession = error("unused")

        override suspend fun login(
            username: String,
            password: String,
        ): AuthSession = error("unused")

        override suspend fun refresh() = session("token")

        override suspend fun refreshAfterUnauthorized(rejectedAccessToken: String) = session("refreshed")

        override suspend fun logout() = Unit

        override fun accessToken() = "token"

        override fun role() = UserRole.MEMBER
    }

    private companion object {
        const val ENTRY = "00000000-0000-4000-8000-000000000001"
        const val ENTRY_2 = "00000000-0000-4000-8000-000000000002"
        const val OWNER = "00000000-0000-4000-8000-000000000003"
        const val TAG_ID = "00000000-0000-4000-8000-000000000004"

        fun tag() = TagItemDto(TAG_ID, "Work")

        fun tag(index: Int) =
            TagItemDto(
                "00000000-0000-4000-8000-${index.toString().padStart(12, '0')}",
                "Tag $index",
            )

        fun favorite() =
            FavoriteItemDto(
                ENTRY,
                "FILE",
                "a.pdf",
                "application/pdf",
                "DOCUMENT",
                1,
                "ACTIVE",
                "2026-08-28T00:00:00Z",
                OwnerSummaryDto(OWNER, "Owner"),
                "OWNER",
                "OWNER",
                null,
                "2026-08-28T00:00:00Z",
            )

        fun session(token: String) =
            AuthSession(
                DeviceId(ENTRY),
                token,
                "refresh-token-with-more-than-thirty-two-characters",
                Instant.MAX,
                Instant.MAX,
                UserRole.MEMBER,
            )
    }
}
