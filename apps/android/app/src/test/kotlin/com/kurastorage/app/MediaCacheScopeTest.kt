package com.kurastorage.app

import com.kurastorage.core.model.ConnectionRoute
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

class MediaCacheScopeTest {
    @Test
    fun mediaCacheScopeIsStableForTheSameServerRouteAndAccount() {
        assertEquals(
            mediaCacheScopeId("storage.example", ConnectionRoute.REMOTE_SECURE, "user-a"),
            mediaCacheScopeId("storage.example", ConnectionRoute.REMOTE_SECURE, "user-a"),
        )
    }

    @Test
    fun mediaCacheScopeSeparatesAccountServerAndRoute() {
        val current = mediaCacheScopeId("storage.example", ConnectionRoute.REMOTE_SECURE, "user-a")

        assertNotEquals(current, mediaCacheScopeId("storage.example", ConnectionRoute.REMOTE_SECURE, "user-b"))
        assertNotEquals(current, mediaCacheScopeId("other.example", ConnectionRoute.REMOTE_SECURE, "user-a"))
        assertNotEquals(current, mediaCacheScopeId("storage.example", ConnectionRoute.LOCAL_DIRECT, "user-a"))
        assertNotEquals(current, mediaCacheScopeId("storage.example", ConnectionRoute.REMOTE_SECURE, null))
    }
}
