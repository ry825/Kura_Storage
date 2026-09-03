package com.kurastorage.core.data.backup

import android.content.ContentResolver
import android.content.Context
import android.database.ContentObserver
import android.database.Cursor
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.provider.DocumentsContract
import android.provider.MediaStore
import com.kurastorage.core.model.backup.BackupSourceType
import com.kurastorage.core.model.backup.LocalBackupRule
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import java.io.Closeable
import java.io.FileNotFoundException
import java.io.InputStream
import java.util.ArrayDeque

private const val MAX_SAF_DEPTH = 64
private const val MAX_SAF_ITEMS = 1_000_000
private const val MILLIS_PER_SECOND = 1_000L
private const val MEDIA_OBSERVER_DEBOUNCE_MILLIS = 1_000L

class AndroidBackupDocumentSource(
    context: Context,
    contentResolver: ContentResolver,
) : BackupDocumentSource {
    private val mediaStore = AndroidMediaStoreDocumentSource(context, contentResolver)
    private val saf = AndroidSafTreeDocumentSource(contentResolver)

    override suspend fun snapshot(rule: LocalBackupRule): SourceSnapshot = delegate(rule).snapshot(rule)

    override suspend fun scan(
        rule: LocalBackupRule,
        afterGeneration: Long?,
        emit: suspend (ScannedDocumentMetadata) -> Unit,
    ): SourceScanOutcome = delegate(rule).scan(rule, afterGeneration, emit)

    private fun delegate(rule: LocalBackupRule): BackupDocumentSource =
        if (rule.sourceType == BackupSourceType.SAF_TREE) saf else mediaStore
}

class AndroidDocumentChecksumSource(
    private val contentResolver: ContentResolver,
) : DocumentChecksumSource {
    override fun open(sourceLocator: String): InputStream =
        contentResolver.openInputStream(Uri.parse(sourceLocator))
            ?: throw FileNotFoundException("Document content is unavailable")
}

class AndroidMediaStoreDocumentSource(
    private val contentQuery: AndroidContentQuery,
    private val snapshotReader: MediaStoreSnapshotReader,
) : BackupDocumentSource {
    constructor(context: Context, contentResolver: ContentResolver) : this(
        ContentResolverQuery(contentResolver),
        MediaStoreSnapshotReader { volume ->
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                SourceSnapshot(MediaStore.getVersion(context, volume), MediaStore.getGeneration(context, volume))
            } else {
                SourceSnapshot(MediaStore.getVersion(context), null)
            }
        },
    )

    override suspend fun snapshot(rule: LocalBackupRule): SourceSnapshot {
        val volume = requireMediaVolume(rule.sourceLocator)
        return snapshotReader.read(volume)
    }

    @Suppress("LongMethod")
    override suspend fun scan(
        rule: LocalBackupRule,
        afterGeneration: Long?,
        emit: suspend (ScannedDocumentMetadata) -> Unit,
    ): SourceScanOutcome {
        require(rule.sourceType != BackupSourceType.SAF_TREE)
        val startSnapshot = snapshot(rule)
        val volume = requireMediaVolume(rule.sourceLocator)
        val collection = mediaCollection(rule.sourceType, volume)
        val projection =
            buildList {
                add(MediaStore.MediaColumns._ID)
                add(MediaStore.MediaColumns.DISPLAY_NAME)
                add(MediaStore.MediaColumns.MIME_TYPE)
                add(MediaStore.MediaColumns.SIZE)
                add(MediaStore.MediaColumns.DATE_MODIFIED)
                add(MediaStore.MediaColumns.DATE_ADDED)
                add(MediaStore.MediaColumns.RELATIVE_PATH)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    add(MediaStore.MediaColumns.GENERATION_ADDED)
                    add(MediaStore.MediaColumns.GENERATION_MODIFIED)
                }
            }.toTypedArray()
        val supportedGeneration = afterGeneration?.takeIf { Build.VERSION.SDK_INT >= Build.VERSION_CODES.R }
        val selection =
            supportedGeneration?.let {
                "${MediaStore.MediaColumns.GENERATION_ADDED} > ? OR " +
                    "${MediaStore.MediaColumns.GENERATION_MODIFIED} > ?"
            }
        val selectionArgs = supportedGeneration?.let { arrayOf(it.toString(), it.toString()) }
        val cursor =
            contentQuery.query(
                collection,
                projection,
                selection,
                selectionArgs,
                "${MediaStore.MediaColumns._ID} ASC",
            ) ?: error("MediaStore query returned no cursor")
        cursor.use {
            val idColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns._ID)
            val nameColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns.DISPLAY_NAME)
            val mimeColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns.MIME_TYPE)
            val sizeColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns.SIZE)
            val modifiedColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns.DATE_MODIFIED)
            val addedColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns.DATE_ADDED)
            val relativeColumn = it.getColumnIndexOrThrow(MediaStore.MediaColumns.RELATIVE_PATH)
            val generationAddedColumn =
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    it.getColumnIndexOrThrow(MediaStore.MediaColumns.GENERATION_ADDED)
                } else {
                    -1
                }
            while (it.moveToNext()) {
                currentCoroutineContext().ensureActive()
                val id = it.getLong(idColumn)
                val displayName = it.getString(nameColumn) ?: error("MediaStore row has no display name")
                val mimeType = it.getString(mimeColumn) ?: error("MediaStore row has no MIME type")
                val size = it.getLong(sizeColumn).also { value -> require(value >= 0) }
                val modifiedAt = it.getLong(modifiedColumn).coerceAtLeast(0) * MILLIS_PER_SECOND
                val addedAt = it.getLong(addedColumn).coerceAtLeast(0)
                val relativeSegments =
                    it
                        .getString(relativeColumn)
                        .orEmpty()
                        .split('/')
                        .filter(String::isNotBlank) + displayName
                val generationAdded = if (generationAddedColumn >= 0) it.getLong(generationAddedColumn) else null
                emit(
                    ScannedDocumentMetadata(
                        providerKey = "media:${rule.sourceType.name}:$volume:$id",
                        identityDiscriminator =
                            generationAdded?.let { value -> "generation:$value" }
                                ?: "legacy:$addedAt:$size:$mimeType",
                        sourceLocator = Uri.withAppendedPath(collection, id.toString()).toString(),
                        relativePath = ScannerPathPolicy.normalize(relativeSegments),
                        displayName = displayName,
                        mimeType = mimeType,
                        size = size,
                        modifiedAtMillis = modifiedAt,
                    ),
                )
            }
        }
        val endSnapshot = snapshot(rule)
        return SourceScanOutcome(completed = endSnapshot == startSnapshot, snapshot = endSnapshot)
    }

    private fun requireMediaVolume(sourceLocator: String): String =
        sourceLocator.trim().also {
            require(
                it == MediaStore.VOLUME_EXTERNAL ||
                    Build.VERSION.SDK_INT >= Build.VERSION_CODES.R &&
                    it == MediaStore.VOLUME_EXTERNAL_PRIMARY,
            ) {
                "Unsupported MediaStore volume"
            }
        }

    private fun mediaCollection(
        sourceType: BackupSourceType,
        volume: String,
    ): Uri =
        when (sourceType) {
            BackupSourceType.MEDIA_IMAGES -> MediaStore.Images.Media.getContentUri(volume)
            BackupSourceType.MEDIA_VIDEOS -> MediaStore.Video.Media.getContentUri(volume)
            BackupSourceType.MEDIA_AUDIO -> MediaStore.Audio.Media.getContentUri(volume)
            BackupSourceType.SAF_TREE -> error("SAF is not a MediaStore collection")
        }
}

fun interface MediaStoreSnapshotReader {
    fun read(volume: String): SourceSnapshot
}

class AndroidSafTreeDocumentSource(
    private val contentQuery: AndroidContentQuery,
    private val maximumDepth: Int = MAX_SAF_DEPTH,
    private val maximumItems: Int = MAX_SAF_ITEMS,
) : BackupDocumentSource {
    constructor(contentResolver: ContentResolver) : this(ContentResolverQuery(contentResolver))

    override suspend fun snapshot(rule: LocalBackupRule) = SourceSnapshot(null, null)

    @Suppress("NestedBlockDepth")
    override suspend fun scan(
        rule: LocalBackupRule,
        afterGeneration: Long?,
        emit: suspend (ScannedDocumentMetadata) -> Unit,
    ): SourceScanOutcome {
        require(rule.sourceType == BackupSourceType.SAF_TREE)
        require(afterGeneration == null)
        val treeUri = Uri.parse(rule.sourceLocator)
        val rootId = DocumentsContract.getTreeDocumentId(treeUri)
        val directories = ArrayDeque<SafDirectory>()
        val visitedDirectories = mutableSetOf<String>()
        directories += SafDirectory(rootId, emptyList(), 0)
        var observedItems = 0
        while (directories.isNotEmpty()) {
            currentCoroutineContext().ensureActive()
            val directory = directories.removeFirst()
            require(directory.depth <= maximumDepth) { "SAF tree exceeds maximum depth" }
            require(visitedDirectories.add(directory.documentId)) { "SAF provider returned a directory cycle" }
            val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, directory.documentId)
            val cursor =
                contentQuery.query(childrenUri, SAF_PROJECTION, null, null, null)
                    ?: error("DocumentsProvider query returned no cursor")
            cursor.use {
                val idColumn = it.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DOCUMENT_ID)
                val nameColumn = it.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DISPLAY_NAME)
                val mimeColumn = it.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_MIME_TYPE)
                val sizeColumn = it.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_SIZE)
                val modifiedColumn = it.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_LAST_MODIFIED)
                while (it.moveToNext()) {
                    currentCoroutineContext().ensureActive()
                    observedItems++
                    require(observedItems <= maximumItems) { "SAF tree exceeds maximum item count" }
                    val documentId = it.getString(idColumn) ?: error("DocumentsProvider row has no document ID")
                    val displayName = it.getString(nameColumn) ?: error("DocumentsProvider row has no display name")
                    val mimeType = it.getString(mimeColumn) ?: error("DocumentsProvider row has no MIME type")
                    val path = directory.path + displayName
                    if (mimeType == DocumentsContract.Document.MIME_TYPE_DIR) {
                        directories += SafDirectory(documentId, path, directory.depth + 1)
                    } else {
                        val size = if (it.isNull(sizeColumn)) 0 else it.getLong(sizeColumn)
                        val modified = if (it.isNull(modifiedColumn)) 0 else it.getLong(modifiedColumn)
                        require(size >= 0 && modified >= 0)
                        emit(
                            ScannedDocumentMetadata(
                                providerKey = "saf:${treeUri.authority}:$documentId",
                                identityDiscriminator = "document:$documentId",
                                sourceLocator =
                                    DocumentsContract.buildDocumentUriUsingTree(treeUri, documentId).toString(),
                                relativePath = ScannerPathPolicy.normalize(path),
                                displayName = displayName,
                                mimeType = mimeType,
                                size = size,
                                modifiedAtMillis = modified,
                            ),
                        )
                    }
                }
            }
        }
        return SourceScanOutcome(true, SourceSnapshot(null, null))
    }

    private data class SafDirectory(
        val documentId: String,
        val path: List<String>,
        val depth: Int,
    )

    private companion object {
        val SAF_PROJECTION =
            arrayOf(
                DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE,
                DocumentsContract.Document.COLUMN_SIZE,
                DocumentsContract.Document.COLUMN_LAST_MODIFIED,
            )
    }
}

fun interface AndroidContentQuery {
    fun query(
        uri: Uri,
        projection: Array<String>,
        selection: String?,
        selectionArgs: Array<String>?,
        sortOrder: String?,
    ): Cursor?
}

private class ContentResolverQuery(
    private val contentResolver: ContentResolver,
) : AndroidContentQuery {
    override fun query(
        uri: Uri,
        projection: Array<String>,
        selection: String?,
        selectionArgs: Array<String>?,
        sortOrder: String?,
    ): Cursor? = contentResolver.query(uri, projection, selection, selectionArgs, sortOrder)
}

class DebouncedMediaStoreObserver(
    contentResolver: ContentResolver,
    handler: Handler,
    private val scope: CoroutineScope,
    observedUri: Uri,
    private val onChanged: suspend () -> Unit,
) : Closeable {
    private val dispatcher = DebouncedScanDispatcher(scope, MEDIA_OBSERVER_DEBOUNCE_MILLIS, onChanged)
    private val observer =
        object : ContentObserver(handler) {
            override fun onChange(
                selfChange: Boolean,
                uri: Uri?,
            ) {
                dispatcher.submit()
            }
        }

    private val resolver = contentResolver

    init {
        resolver.registerContentObserver(observedUri, true, observer)
    }

    override fun close() {
        dispatcher.close()
        resolver.unregisterContentObserver(observer)
    }
}

class DebouncedScanDispatcher(
    private val scope: CoroutineScope,
    private val delayMillis: Long,
    private val dispatch: suspend () -> Unit,
) : Closeable {
    private var pending: Job? = null

    init {
        require(delayMillis >= 0)
    }

    fun submit() {
        pending?.cancel()
        pending =
            scope.launch {
                delay(delayMillis)
                dispatch()
            }
    }

    override fun close() {
        pending?.cancel()
    }
}
