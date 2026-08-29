package com.kurastorage.core.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class OrganizationModelsTest {
    @Test
    fun tagName_normalizesTrimAndNfc() {
        assertEquals("é", validateTagName("  e\u0301  ").value?.value)
    }

    @Test
    fun tagName_rejectsEmptyControlAndFiftyOneCodePoints() {
        assertEquals(TagNameValidationError.EMPTY, validateTagName("  ").error)
        assertEquals(TagNameValidationError.CONTROL_CHARACTER, validateTagName("a\u0000b").error)
        assertEquals(TagNameValidationError.TOO_LONG, validateTagName("😀".repeat(51)).error)
        assertNotNull(validateTagName("😀".repeat(50)).value)
    }

    @Test
    fun searchTagIds_validateZeroOneTenElevenDuplicatesAndUuid() {
        val ids = (1..11).map { "00000000-0000-0000-0000-${it.toString().padStart(12, '0')}" }
        assertEquals(SearchValidationError.QUERY_REQUIRED, SearchInput().validate().error)
        assertNotNull(SearchInput(tagIds = ids.take(1)).validate().value)
        assertNotNull(SearchInput(tagIds = ids.take(10)).validate().value)
        assertEquals(SearchValidationError.INVALID_FILTER, SearchInput(tagIds = ids).validate().error)
        assertEquals(
            SearchValidationError.INVALID_FILTER,
            SearchInput(tagIds = listOf(ids[0], ids[0])).validate().error,
        )
        assertNull(SearchInput(tagIds = listOf("invalid")).validate().value)
    }
}
