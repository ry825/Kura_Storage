package com.kurastorage.app

import com.kurastorage.core.data.backup.BackupProgressSnapshot
import com.kurastorage.core.model.backup.SyncLifecycleState
import com.kurastorage.core.ui.AppDestination
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class AppNavigationTest {
    @Test
    fun `top-level routes are classified without exposing secondary or protected routes`() {
        assertEquals(TopLevelDestination.HOME, topLevelDestinationFor(AppDestination.HOME.route))
        assertEquals(TopLevelDestination.FILES, topLevelDestinationFor(AppDestination.FILES.route))
        assertEquals(TopLevelDestination.SHARING, topLevelDestinationFor(AppDestination.SHARING.route))
        assertEquals(TopLevelDestination.SEARCH, topLevelDestinationFor("search?category={category}"))
        assertEquals(TopLevelDestination.SETTINGS, topLevelDestinationFor(AppDestination.SETTINGS.route))
        assertNull(topLevelDestinationFor(AppDestination.AUTHENTICATION.route))
        assertNull(topLevelDestinationFor(AppDestination.CACHE_MANAGEMENT.route))
        assertNull(topLevelDestinationFor("${AppDestination.PHOTO_VIEWER.route}/{contextId}/{fileId}"))
        assertNull(topLevelDestinationFor(null))
    }

    @Test
    fun `a changed or cleared session replaces protected UI state`() {
        assertFalse(shouldReplaceSession(null, "session-a"))
        assertFalse(shouldReplaceSession("session-a", "session-a"))
        assertTrue(shouldReplaceSession("session-a", "session-b"))
        assertTrue(shouldReplaceSession("session-a", null))
    }

    @Test
    fun `home backup summary separates pending uploading and failed work`() {
        val completedAt = Instant.parse("2026-09-03T10:00:00Z")
        val summary =
            HomeBackupSummary.from(
                BackupProgressSnapshot(
                    stateCounts =
                        mapOf(
                            SyncLifecycleState.DISCOVERED to 1,
                            SyncLifecycleState.PENDING to 2,
                            SyncLifecycleState.READY_TO_UPLOAD to 3,
                            SyncLifecycleState.UPLOADING to 4,
                            SyncLifecycleState.FAILED to 5,
                        ),
                    ruleStateCounts = emptyMap(),
                    waitReasonCounts = emptyMap(),
                    lastCompletedAt = completedAt,
                ),
            )

        assertEquals(6, summary.pendingCount)
        assertEquals(4, summary.uploadingCount)
        assertEquals(5, summary.failedCount)
        assertEquals(completedAt, summary.lastCompletedAt)
        assertEquals("Needs attention", summary.statusLabel)
    }
}
