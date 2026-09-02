package com.kurastorage.app

import com.kurastorage.core.model.ActivityDeleteKind
import com.kurastorage.core.model.ActivityDetail
import com.kurastorage.core.model.ActivityItem
import com.kurastorage.core.model.ActivityTargetType
import com.kurastorage.core.model.UserActivityType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.Instant

class ActivityNavigationTest {
    @Test fun `activity state is isolated by session`() {
        assertEquals("activity-session-a", activityViewModelKey("session-a"))
        assertFalse(activityViewModelKey("session-a") == activityViewModelKey("session-b"))
    }

    @Test fun `only currently accessible known targets receive an ID-only route`() {
        val available = item(ID, ActivityTargetType.FILE)
        val route = activityTargetRoute(available)

        assertEquals("shared-entry/$ID/FILE", route)
        assertFalse(checkNotNull(route).contains(available.targetName))
        assertNull(activityTargetRoute(item(null, ActivityTargetType.FILE)))
        assertNull(activityTargetRoute(item(ID, ActivityTargetType.UNKNOWN)))
    }

    private fun item(
        id: String?,
        targetType: ActivityTargetType,
    ) = ActivityItem(
        UserActivityType.DELETE,
        Instant.EPOCH,
        "Actor",
        null,
        id,
        targetType,
        "private-name.txt",
        "Owner",
        ActivityDetail.Delete(ActivityDeleteKind.TRASHED),
    )

    private companion object {
        const val ID = "00000000-0000-4000-8000-000000000001"
    }
}
