package com.kurastorage.core.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ActivityModelsTest {
    @Test
    fun `wire enums fail closed without retaining unknown values`() {
        assertEquals(UserActivityType.UPLOAD, UserActivityType.fromWire("UPLOAD"))
        assertEquals(UserActivityType.UNKNOWN, UserActivityType.fromWire("FUTURE_SECRET_TYPE"))
        assertEquals(ActivityTargetType.UNKNOWN, ActivityTargetType.fromWire("DEVICE"))
        assertEquals(ActivityEditKind.UNKNOWN, ActivityEditKind.fromWire("BINARY_PATCH"))
        assertEquals(ActivityShareAction.UNKNOWN, ActivityShareAction.fromWire("INVITED"))
        assertEquals(ActivityDeleteKind.UNKNOWN, ActivityDeleteKind.fromWire("ARCHIVED"))
    }

    @Test
    fun `activity page exposes opaque cursor and accessible target only`() {
        val page = ActivityPage(emptyList(), "opaque-next")

        assertEquals("opaque-next", page.nextCursor)
        assertNull(page.items.firstOrNull()?.targetEntryId)
    }
}
