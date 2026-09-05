package com.kurastorage.core.data.media

import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.Closeable
import java.io.File
import java.io.FileOutputStream
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.time.Clock
import java.time.Duration

class PdfTooLargeException : IllegalArgumentException("PDF exceeds the viewer limit")

class InsufficientPdfStorageException : IllegalStateException("Not enough private cache space")

class InvalidPdfException : IllegalStateException("Downloaded content is not a valid PDF")

class TemporaryPdfStore(
    cacheRoot: File,
    private val scopeId: String,
    private val repository: MediaRepository,
    private val availableBytes: (File) -> Long = File::getUsableSpace,
    private val clock: Clock = Clock.systemUTC(),
) : Closeable {
    private val directory = File(cacheRoot, "media-pdf/$scopeId")
    private val leases = mutableMapOf<File, Int>()

    init {
        require(scopeId.isNotBlank())
        directory.mkdirs()
        require(directory.isDirectory) { "Unable to create the PDF cache directory" }
    }

    @Suppress("CyclomaticComplexMethod", "TooGenericExceptionCaught")
    suspend fun download(
        fileId: String,
        fileVersion: Long,
        metadata: OriginalMetadata,
    ): File =
        withContext(Dispatchers.IO) {
            val downloadContext = currentCoroutineContext()
            validateMetadata(metadata)
            cleanupExpired()

            val stem = safeStem(fileId, fileVersion)
            val partial = File(directory, "$stem.pdf.part")
            val complete = File(directory, "$stem.pdf")
            if (complete.isFile && complete.length() == metadata.size.value && hasPdfSignature(complete)) {
                complete.setLastModified(clock.millis())
                return@withContext complete
            }
            ensureSessionCapacity(metadata.size.value)
            if (availableBytes(directory) < metadata.size.value + RESERVED_FREE_BYTES) {
                throw InsufficientPdfStorageException()
            }
            partial.delete()
            try {
                when (val result = repository.openContent(fileId, MediaVariant.ORIGINAL)) {
                    is MediaContentResult.Generating -> throw KuraStorageException.InvalidServerResponse()
                    is MediaContentResult.Ready ->
                        result.content.use { content ->
                            FileOutputStream(partial).use { output ->
                                val copied = content.copyTo(output, MAX_FILE_BYTES) { downloadContext.ensureActive() }
                                if (copied != metadata.size.value) throw InvalidPdfException()
                                output.fd.sync()
                            }
                        }
                }
                if (!hasPdfSignature(partial)) throw InvalidPdfException()
                java.nio.file.Files
                    .move(partial.toPath(), complete.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
                complete.setLastModified(clock.millis())
                complete
            } catch (error: Throwable) {
                partial.delete()
                throw error
            }
        }

    suspend fun acquire(file: File): PdfFileLease = withContext(Dispatchers.IO) { acquireBlocking(file) }

    @Synchronized
    private fun acquireBlocking(file: File): PdfFileLease {
        require(file.parentFile == directory && file.isFile) { "PDF is outside this session" }
        leases[file] = (leases[file] ?: 0) + 1
        file.setLastModified(clock.millis())
        return PdfFileLease(file) { release(file) }
    }

    @Synchronized
    fun cleanupExpired() {
        val cutoff = clock.millis() - UNREFERENCED_TTL.toMillis()
        directory
            .listFiles()
            .orEmpty()
            .filter { it.isFile && it !in leases && (it.name.endsWith(".part") || it.lastModified() < cutoff) }
            .forEach(File::delete)
    }

    @Synchronized
    override fun close() {
        leases.clear()
        directory.deleteRecursively()
    }

    private fun validateMetadata(metadata: OriginalMetadata) {
        if (metadata.mimeType
                .substringBefore(';')
                .trim()
                .lowercase() != PDF_MIME
        ) {
            throw InvalidPdfException()
        }
        if (metadata.size.value > MAX_FILE_BYTES) throw PdfTooLargeException()
    }

    @Synchronized
    private fun ensureSessionCapacity(incomingBytes: Long) {
        var total =
            directory
                .listFiles()
                .orEmpty()
                .filter(File::isFile)
                .sumOf(File::length)
        if (total + incomingBytes <= MAX_SESSION_BYTES) return
        val candidates =
            directory
                .listFiles()
                .orEmpty()
                .filter { it.isFile && it !in leases && !it.name.endsWith(".part") }
                .sortedBy(File::lastModified)
        for (candidate in candidates) {
            val length = candidate.length()
            if (candidate.delete()) total -= length
            if (total + incomingBytes <= MAX_SESSION_BYTES) return
        }
        throw InsufficientPdfStorageException()
    }

    @Synchronized
    private fun release(file: File) {
        val remaining = (leases[file] ?: return) - 1
        if (remaining <= 0) leases.remove(file) else leases[file] = remaining
    }

    private fun safeStem(
        fileId: String,
        fileVersion: Long,
    ): String {
        require(fileId.isNotBlank() && fileVersion >= 0)
        return MessageDigest
            .getInstance("SHA-256")
            .digest("$scopeId:$fileId:$fileVersion".toByteArray())
            .joinToString("") { "%02x".format(it) }
    }

    private fun hasPdfSignature(file: File): Boolean =
        file.inputStream().use { input ->
            val signature = ByteArray(PDF_SIGNATURE.size)
            input.read(signature) == signature.size && signature.contentEquals(PDF_SIGNATURE)
        }

    companion object {
        const val MAX_FILE_BYTES = 256L * 1024 * 1024
        const val MAX_SESSION_BYTES = 512L * 1024 * 1024
        const val RESERVED_FREE_BYTES = 64L * 1024 * 1024
        val UNREFERENCED_TTL: Duration = Duration.ofHours(1)
        private const val PDF_MIME = "application/pdf"
        private val PDF_SIGNATURE = "%PDF-".toByteArray()

        fun cleanupPreviousSessions(cacheRoot: File) {
            File(cacheRoot, "media-pdf").deleteRecursively()
        }
    }
}

class PdfFileLease internal constructor(
    val file: File,
    private val release: () -> Unit,
) : Closeable {
    private var closed = false

    @Synchronized
    override fun close() {
        if (closed) return
        closed = true
        release()
    }
}
