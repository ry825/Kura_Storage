package com.kurastorage.core.data.backup

import android.Manifest
import com.kurastorage.core.database.backup.ExternalWifiPolicyDao
import com.kurastorage.core.database.backup.ExternalWifiPolicyEntity
import com.kurastorage.core.model.backup.AccountScopeId
import com.kurastorage.core.model.backup.ExternalWifiPolicy
import com.kurastorage.core.model.backup.ExternalWifiPolicyId
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

class ExternalWifiPolicyRepositoryTest {
    private val scope = AccountScopeId("a".repeat(64))

    @Test
    fun normalizesQuotedSsidAndHyphenatedBssid() {
        assertEquals("Family Wi-Fi", WifiIdentifierNormalizer.normalizeSsid("\"Family Wi-Fi\""))
        assertEquals("aa:bb:cc:dd:ee:ff", WifiIdentifierNormalizer.normalizeBssid("AA-BB-CC-DD-EE-FF"))
    }

    @Test
    fun rejectsUnknownSsidPlaceholderBssidAndControlCharacters() {
        assertThrows(IllegalArgumentException::class.java) { WifiIdentifierNormalizer.normalizeSsid("<unknown ssid>") }
        assertThrows(IllegalArgumentException::class.java) { WifiIdentifierNormalizer.normalizeSsid("wifi\nname") }
        assertThrows(IllegalArgumentException::class.java) { WifiIdentifierNormalizer.normalizeSsid("w".repeat(33)) }
        assertThrows(IllegalArgumentException::class.java) {
            WifiIdentifierNormalizer.normalizeBssid("02:00:00:00:00:00")
        }
    }

    @Test
    fun permissionPolicyFailsClosedAcrossAndroidVersions() {
        assertEquals(setOf(Manifest.permission.ACCESS_FINE_LOCATION), WifiPermissionPolicy.requiredPermissions(30))
        assertEquals(
            setOf(
                Manifest.permission.NEARBY_WIFI_DEVICES,
                Manifest.permission.ACCESS_COARSE_LOCATION,
                Manifest.permission.ACCESS_FINE_LOCATION,
            ),
            WifiPermissionPolicy.requiredPermissions(33),
        )
        assertEquals(
            setOf(Manifest.permission.ACCESS_COARSE_LOCATION, Manifest.permission.ACCESS_FINE_LOCATION),
            WifiPermissionPolicy.requiredPermissions(32),
        )
        assertEquals(
            CurrentWifiResult.PermissionPermanentlyDenied,
            WifiPermissionPolicy.missingResult(setOf(Manifest.permission.ACCESS_FINE_LOCATION)) { false },
        )
        assertEquals(
            CurrentWifiResult.PermissionRequired(setOf(Manifest.permission.ACCESS_FINE_LOCATION)),
            WifiPermissionPolicy.missingResult(setOf(Manifest.permission.ACCESS_FINE_LOCATION)) { true },
        )
    }

    @Test
    fun disabledOrMeteredPolicyNeverPermitsAutomaticTransfer() {
        val repository = repository(ConnectedWifi("Home", null, false))
        val base = policy(enabled = true, metered = false)
        assertTrue(repository.matchesCurrentWifi(base, ConnectedWifi("\"Home\"", null, false)))
        assertFalse(repository.matchesCurrentWifi(base.copy(enabled = false), ConnectedWifi("Home", null, false)))
        assertFalse(repository.matchesCurrentWifi(base.copy(treatAsMetered = true), ConnectedWifi("Home", null, false)))
        assertFalse(repository.matchesCurrentWifi(base, ConnectedWifi("Home", null, true)))
    }

    @Test
    fun bssidRestrictionRequiresBothIdentifiers() {
        val repository = repository(ConnectedWifi("Home", "aa:bb:cc:dd:ee:ff", false))
        val restricted = policy(enabled = true, metered = false).copy(normalizedBssid = "aa:bb:cc:dd:ee:ff")
        assertTrue(repository.matchesCurrentWifi(restricted, ConnectedWifi("Home", "AA-BB-CC-DD-EE-FF", false)))
        assertFalse(repository.matchesCurrentWifi(restricted, ConnectedWifi("Home", "11:22:33:44:55:66", false)))
    }

    @Test
    fun registrationUsesOnlyCurrentWifiAndCombinesSystemMeteredState() =
        runTest {
            val dao = FakeWifiDao()
            val repository = repository(ConnectedWifi("\"Cafe\"", "AA-BB-CC-DD-EE-FF", true), dao)
            val created = repository.registerCurrent(scope, "Cafe", restrictToBssid = true, treatAsMetered = false)
            assertEquals("Cafe", created.normalizedSsid)
            assertEquals("aa:bb:cc:dd:ee:ff", created.normalizedBssid)
            assertTrue(created.treatAsMetered)
            assertEquals(scope.value, dao.saved?.accountScopeId)
        }

    @Test
    fun duplicateRegistrationAndUnavailableRestrictedBssidFailClosed() =
        runTest {
            val duplicateDao = FakeWifiDao()
            val duplicateRepository = repository(ConnectedWifi("Home", null, false), duplicateDao)
            duplicateRepository.registerCurrent(scope, "Home", restrictToBssid = false, treatAsMetered = false)

            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking {
                    duplicateRepository.registerCurrent(
                        scope,
                        "Home again",
                        restrictToBssid = false,
                        treatAsMetered = false,
                    )
                }
            }
            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking {
                    repository(ConnectedWifi("Home", null, false)).registerCurrent(
                        scope,
                        "Restricted",
                        restrictToBssid = true,
                        treatAsMetered = false,
                    )
                }
            }
        }

    @Test
    fun editingPolicyCannotCrossAccountScope() =
        runTest {
            val dao = FakeWifiDao()
            val repository = repository(ConnectedWifi("Home", null, false), dao)
            val created = repository.registerCurrent(scope, "Home", false, false)

            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking {
                    repository.save(AccountScopeId("b".repeat(64)), created)
                }
            }
        }

    @Test
    fun registrationRejectsPolicyCountAboveLimit() =
        runTest {
            val dao = FakeWifiDao().apply { currentCount = 50 }
            val repository = repository(ConnectedWifi("Home", null, false), dao)

            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking {
                    repository.registerCurrent(scope, "Home", false, false)
                }
            }
        }

    private fun repository(
        wifi: ConnectedWifi,
        dao: FakeWifiDao = FakeWifiDao(),
    ) = RoomExternalWifiPolicyRepository(
        dao,
        CurrentWifiSource { CurrentWifiResult.Connected(wifi) },
        Clock.fixed(Instant.EPOCH, ZoneOffset.UTC),
    )

    private fun policy(
        enabled: Boolean,
        metered: Boolean,
    ) = ExternalWifiPolicy(
        ExternalWifiPolicyId(UUID.randomUUID().toString()),
        scope,
        "Home",
        "Home",
        null,
        metered,
        enabled,
        Instant.EPOCH,
        Instant.EPOCH,
    )
}

private class FakeWifiDao : ExternalWifiPolicyDao {
    var saved: ExternalWifiPolicyEntity? = null
    var currentCount = 0

    override fun observeByScope(scopeId: String): Flow<List<ExternalWifiPolicyEntity>> = flowOf(listOfNotNull(saved))

    override suspend fun listByScope(scopeId: String): List<ExternalWifiPolicyEntity> =
        listOfNotNull(saved?.takeIf { it.accountScopeId == scopeId })

    override suspend fun count(scopeId: String): Int = currentCount

    override suspend fun find(
        id: String,
        scopeId: String,
    ): ExternalWifiPolicyEntity? = saved?.takeIf { it.id == id && it.accountScopeId == scopeId }

    override suspend fun findByNetwork(
        scopeId: String,
        normalizedSsid: String,
        normalizedBssidKey: String,
    ): ExternalWifiPolicyEntity? =
        saved?.takeIf {
            it.accountScopeId == scopeId &&
                it.normalizedSsid == normalizedSsid &&
                it.normalizedBssidKey == normalizedBssidKey
        }

    override suspend fun upsert(policy: ExternalWifiPolicyEntity) {
        saved = policy
        currentCount = 1
    }

    override suspend fun delete(
        id: String,
        scopeId: String,
    ): Int = if (saved?.id == id && saved?.accountScopeId == scopeId) 1 else 0
}
