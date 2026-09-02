package com.kurastorage.feature.backup

import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.BackupRuleId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Test
import java.util.UUID

class BackupWorkNamesTest {
    @Test
    fun namesAreStableScopedAndDoNotExposeIdentifiers() {
        val scope = AccountScopeId("a".repeat(64))
        val rule = BackupRuleId(UUID.randomUUID().toString())
        val first = BackupWorkNames.scan(scope, rule)
        assertEquals(first, BackupWorkNames.scan(scope, rule))
        assertNotEquals(first, BackupWorkNames.scan(scope, BackupRuleId(UUID.randomUUID().toString())))
        assertFalse(first.contains(scope.value))
        assertFalse(first.contains(rule.value))
    }
}
