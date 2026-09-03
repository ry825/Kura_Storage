package com.kurastorage.core.data.backup

import com.kurastorage.core.database.backup.BackupScanPersistence
import com.kurastorage.core.database.backup.ScanPersistenceCandidate
import com.kurastorage.core.model.backup.BackupRuleId
import com.kurastorage.core.model.backup.LocalBackupRule
import com.kurastorage.core.model.backup.ScanCheckpoint
import java.security.MessageDigest
import java.time.Instant

private const val UNSIGNED_BYTE_MASK = 0xff

class RoomBackupScanStore(
    private val persistence: BackupScanPersistence,
) : BackupScanStore {
    override suspend fun checkpoint(ruleId: BackupRuleId): ScanCheckpoint? = persistence.checkpoint(ruleId)

    override suspend fun existing(
        rule: LocalBackupRule,
        document: ScannedDocumentMetadata,
    ): StoredDocumentMetadata? =
        persistence.existing(rule, document.providerKey)?.let {
            StoredDocumentMetadata(
                localDocumentKey = it.localDocumentKey,
                identityDiscriminator = it.identityDiscriminator,
                relativePath = it.relativePath,
                displayName = it.displayName,
                size = it.size,
                modifiedAtMillis = it.modifiedAtMillis,
                checksum = it.checksum,
            )
        }

    override suspend fun applyBatch(
        rule: LocalBackupRule,
        documents: List<PreparedScannedDocument>,
        observedAt: Instant,
    ) = persistence.applyBatch(
        rule,
        documents.map { prepared ->
            val document = prepared.metadata
            ScanPersistenceCandidate(
                providerKey = document.providerKey,
                identityDiscriminator = document.identityDiscriminator,
                localDocumentKey = prepared.localDocumentKey,
                sourceLocator = document.sourceLocator,
                relativePath = document.relativePath,
                displayName = document.displayName,
                size = document.size,
                modifiedAtMillis = document.modifiedAtMillis,
                checksum = prepared.checksum,
                sourceFingerprint = fingerprint(document),
            )
        },
        observedAt,
    )

    override suspend fun complete(
        rule: LocalBackupRule,
        snapshot: SourceSnapshot,
        observedAt: Instant,
        wasFullScan: Boolean,
    ) = persistence.complete(rule, snapshot.version, snapshot.generation, observedAt, wasFullScan)

    private fun fingerprint(document: ScannedDocumentMetadata): String {
        val value =
            listOf(
                document.identityDiscriminator,
                document.relativePath,
                document.displayName,
                document.mimeType,
                document.size.toString(),
                document.modifiedAtMillis.toString(),
            ).joinToString("\u0000")
        return MessageDigest
            .getInstance("SHA-256")
            .digest(value.encodeToByteArray())
            .joinToString("") { byte -> "%02x".format(byte.toInt() and UNSIGNED_BYTE_MASK) }
    }
}
