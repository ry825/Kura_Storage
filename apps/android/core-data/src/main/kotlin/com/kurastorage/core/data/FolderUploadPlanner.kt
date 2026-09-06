package com.kurastorage.core.data

import android.content.ContentResolver
import android.database.Cursor
import android.net.Uri
import android.provider.DocumentsContract
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.util.ArrayDeque

data class FolderUploadDocument(
    val documentId: String,
    val displayName: String,
    val isDirectory: Boolean,
    val size: Long,
    val contentType: String?,
    val sourceUri: String,
    val readable: Boolean,
    val withinTree: Boolean,
)

sealed interface FolderUploadEntry {
    val documentId: String
    val relativeSegments: List<String>

    data class Folder(
        override val documentId: String,
        override val relativeSegments: List<String>,
    ) : FolderUploadEntry

    data class File(
        override val documentId: String,
        override val relativeSegments: List<String>,
        val sourceUri: String,
        val size: Long,
        val contentType: String?,
    ) : FolderUploadEntry
}

enum class FolderUploadFailure {
    INVALID_DOCUMENT_ID,
    INVALID_NAME,
    INVALID_SIZE,
    UNREADABLE,
    OUTSIDE_TREE,
    DUPLICATE_DOCUMENT,
}

data class FolderUploadRejection(
    val documentId: String,
    val relativeSegments: List<String>,
    val reason: FolderUploadFailure,
)

data class FolderUploadPlan(
    val entries: List<FolderUploadEntry>,
    val rejections: List<FolderUploadRejection>,
)

interface FolderUploadTreeSource {
    suspend fun root(treeUri: String): FolderUploadDocument

    suspend fun children(
        treeUri: String,
        parentDocumentId: String,
    ): List<FolderUploadDocument>
}

class FolderUploadPlanner(
    private val source: FolderUploadTreeSource,
    private val maximumDepth: Int = 64,
    private val maximumItems: Int = 100_000,
) {
    init {
        require(maximumDepth >= 0)
        require(maximumItems > 0)
    }

    @Suppress("LongMethod", "CyclomaticComplexMethod")
    suspend fun plan(treeUri: String): FolderUploadPlan {
        require(treeUri.isNotBlank())
        val root = source.root(treeUri)
        require(
            root.documentId.isSafeDocumentId() &&
                root.isDirectory &&
                root.withinTree &&
                root.readable &&
                root.displayName.isSafeSegment(),
        ) {
            "Selected SAF root is invalid or unreadable"
        }
        val entries = mutableListOf<FolderUploadEntry>()
        val rejections = mutableListOf<FolderUploadRejection>()
        val visited = mutableSetOf(root.documentId)
        val directories = ArrayDeque<PendingDirectory>()
        val rootPath = listOf(root.displayName)
        entries += FolderUploadEntry.Folder(root.documentId, rootPath)
        directories += PendingDirectory(root.documentId, rootPath, 0)
        var observed = 1

        while (directories.isNotEmpty()) {
            currentCoroutineContext().ensureActive()
            val directory = directories.removeFirst()
            require(directory.depth <= maximumDepth) { "Folder upload exceeds maximum depth" }
            source.children(treeUri, directory.documentId).forEach { child ->
                currentCoroutineContext().ensureActive()
                observed++
                require(observed <= maximumItems) { "Folder upload exceeds maximum item count" }
                val path = directory.path + child.displayName
                val failure =
                    when {
                        !child.withinTree -> FolderUploadFailure.OUTSIDE_TREE
                        !child.documentId.isSafeDocumentId() -> FolderUploadFailure.INVALID_DOCUMENT_ID
                        !child.displayName.isSafeSegment() -> FolderUploadFailure.INVALID_NAME
                        !visited.add(child.documentId) -> FolderUploadFailure.DUPLICATE_DOCUMENT
                        !child.readable -> FolderUploadFailure.UNREADABLE
                        !child.isDirectory && child.size < 0 -> FolderUploadFailure.INVALID_SIZE
                        else -> null
                    }
                if (failure != null) {
                    rejections += FolderUploadRejection(child.documentId, path, failure)
                } else if (child.isDirectory) {
                    val depth = directory.depth + 1
                    require(depth <= maximumDepth) { "Folder upload exceeds maximum depth" }
                    entries += FolderUploadEntry.Folder(child.documentId, path)
                    directories += PendingDirectory(child.documentId, path, depth)
                } else {
                    entries +=
                        FolderUploadEntry.File(
                            child.documentId,
                            path,
                            child.sourceUri,
                            child.size,
                            child.contentType,
                        )
                }
            }
        }
        return FolderUploadPlan(entries, rejections)
    }

    private data class PendingDirectory(
        val documentId: String,
        val path: List<String>,
        val depth: Int,
    )
}

fun interface FolderDocumentQuery {
    fun query(
        uri: Uri,
        projection: Array<String>,
    ): Cursor?
}

fun interface FolderDocumentReadability {
    fun canRead(uri: Uri): Boolean
}

class AndroidFolderUploadTreeSource(
    private val query: FolderDocumentQuery,
    private val readability: FolderDocumentReadability,
) : FolderUploadTreeSource {
    constructor(resolver: ContentResolver) : this(
        query = FolderDocumentQuery { uri, projection -> resolver.query(uri, projection, null, null, null) },
        readability =
            FolderDocumentReadability { uri ->
                try {
                    resolver.openInputStream(uri)?.use { } != null
                } catch (_: Exception) {
                    false
                }
            },
    )

    override suspend fun root(treeUri: String): FolderUploadDocument {
        val tree = Uri.parse(treeUri)
        val rootId = DocumentsContract.getTreeDocumentId(tree)
        val rootUri = DocumentsContract.buildDocumentUriUsingTree(tree, rootId)
        return requireNotNull(readOne(tree, rootUri, rootId)) { "Selected SAF root is unavailable" }
    }

    override suspend fun children(
        treeUri: String,
        parentDocumentId: String,
    ): List<FolderUploadDocument> {
        val tree = Uri.parse(treeUri)
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentDocumentId)
        val result = mutableListOf<FolderUploadDocument>()
        val cursor =
            query.query(childrenUri, PROJECTION)
                ?: error("DocumentsProvider query returned no cursor")
        cursor.use {
            while (it.moveToNext()) result += it.toDocument(tree)
        }
        return result
    }

    private fun readOne(
        tree: Uri,
        documentUri: Uri,
        expectedId: String,
    ): FolderUploadDocument? {
        val cursor = query.query(documentUri, PROJECTION) ?: return null
        return cursor.use {
            if (!it.moveToFirst()) return@use null
            it.toDocument(tree).takeIf { document -> document.documentId == expectedId }
        }
    }

    private fun Cursor.toDocument(tree: Uri): FolderUploadDocument {
        val id = getString(getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DOCUMENT_ID))
        val name = getString(getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DISPLAY_NAME)).orEmpty()
        val mime = getString(getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_MIME_TYPE))
        val isDirectory = mime == DocumentsContract.Document.MIME_TYPE_DIR
        val sizeColumn = getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_SIZE)
        val size = if (isNull(sizeColumn)) -1 else getLong(sizeColumn)
        val uri = DocumentsContract.buildDocumentUriUsingTree(tree, id)
        val readable =
            if (isDirectory) {
                true
            } else {
                readability.canRead(uri)
            }
        return FolderUploadDocument(
            documentId = id,
            displayName = name,
            isDirectory = isDirectory,
            size = if (isDirectory) 0 else size,
            contentType = mime.takeUnless { isDirectory },
            sourceUri = uri.toString(),
            readable = readable,
            withinTree = uri.authority == tree.authority,
        )
    }

    private companion object {
        val PROJECTION =
            arrayOf(
                DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE,
                DocumentsContract.Document.COLUMN_SIZE,
            )
    }
}

private fun String.isSafeSegment(): Boolean =
    isNotBlank() &&
        this !in setOf(".", "..") &&
        none { it == '/' || it == '\\' || it.code < MIN_PRINTABLE_CHARACTER_CODE }

private fun String.isSafeDocumentId(): Boolean = isNotBlank() && none { it.code < MIN_PRINTABLE_CHARACTER_CODE }

private const val MIN_PRINTABLE_CHARACTER_CODE = 32
