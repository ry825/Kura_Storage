@file:Suppress("MaxLineLength", "MagicNumber")

package com.kurastorage.feature.text

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.FileRepository
import com.kurastorage.core.data.TextFileRepository
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.SupportedTextMimeTypes
import com.kurastorage.core.model.TextConflict
import com.kurastorage.core.model.TextDocument
import com.kurastorage.core.model.canEditText
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.util.UUID

enum class TextEditorPhase { LOADING, VIEWING, EDITING, SAVING, SAVED, CONFLICT, ERROR }

data class TextEditorUiState(
    val file: FileEntry? = null,
    val document: TextDocument? = null,
    val draft: String = "",
    val phase: TextEditorPhase = TextEditorPhase.LOADING,
    val dirty: Boolean = false,
    val canEdit: Boolean = false,
    val conflict: TextConflict? = null,
    val diff: List<LineDiff> = emptyList(),
    val diffTruncated: Boolean = false,
    val conflictReloadFailed: Boolean = false,
    val errorCode: ErrorCode? = null,
    val requestId: String? = null,
    val showDiscardConfirmation: Boolean = false,
    val draftPersisted: Boolean = true,
    val forceOverwriteAvailable: Boolean = false,
    val exitAfterSave: Boolean = false,
)

@Suppress("TooManyFunctions")
class TextEditorViewModel(
    private val fileId: String,
    private val files: FileRepository,
    private val text: TextFileRepository,
    private val savedState: SavedStateHandle,
    private val operationIdFactory: () -> String = { UUID.randomUUID().toString() },
) : ViewModel() {
    private val mutableState = MutableStateFlow(TextEditorUiState())
    private var requestGeneration = 0L
    private var requestJob: Job? = null
    private var pendingOperationId: String? = null

    val state: StateFlow<TextEditorUiState> = mutableState.asStateFlow()

    init {
        load()
    }

    fun load() {
        requestJob?.cancel()
        val generation = ++requestGeneration
        mutableState.value = TextEditorUiState()
        requestJob =
            viewModelScope.launch {
                runCatching {
                    val file = files.detail(fileId)
                    require(file.isSupportedText())
                    file to text.current(fileId)
                }.onSuccess { (file, document) ->
                    if (generation != requestGeneration) return@onSuccess
                    val restoredDraft = restoredDraft(document)
                    mutableState.value =
                        TextEditorUiState(
                            file = file,
                            document = document,
                            draft = restoredDraft ?: document.content,
                            phase = if (restoredDraft == null) TextEditorPhase.VIEWING else TextEditorPhase.EDITING,
                            dirty = restoredDraft != null && restoredDraft != document.content,
                            canEdit = canEditText(file.permission, file.permissionSource),
                        )
                }.onFailure { error ->
                    if (generation == requestGeneration) mutableState.value = errorState(error)
                }
            }
    }

    fun beginEditing() {
        val current = mutableState.value
        if (!current.canEdit || current.document == null) return
        mutableState.value = current.copy(phase = TextEditorPhase.EDITING)
    }

    fun endEditing() {
        val current = mutableState.value
        if (current.phase !in setOf(TextEditorPhase.EDITING, TextEditorPhase.SAVED, TextEditorPhase.ERROR)) return
        mutableState.value = current.copy(phase = TextEditorPhase.VIEWING)
    }

    fun updateDraft(value: String) {
        val current = mutableState.value
        if (!current.canEdit || current.phase !in EDITABLE_PHASES) return
        val dirty = value != current.document?.content
        val persisted = persistDraft(value, current.document?.fileVersion)
        if (value != current.draft) pendingOperationId = null
        mutableState.value =
            current.copy(
                draft = value,
                dirty = dirty,
                phase = TextEditorPhase.EDITING,
                draftPersisted = persisted,
                conflict = null,
                diff = emptyList(),
                diffTruncated = false,
                conflictReloadFailed = false,
            )
    }

    @Suppress("ReturnCount", "TooGenericExceptionCaught")
    fun save() {
        val snapshot = mutableState.value
        val base = snapshot.document ?: return
        if (!snapshot.dirty || snapshot.phase == TextEditorPhase.SAVING) return
        if (!SupportedTextMimeTypes.isWithinSizeLimit(snapshot.draft)) {
            mutableState.value = snapshot.copy(phase = TextEditorPhase.ERROR, errorCode = ErrorCode.TEXT_SIZE_LIMIT_EXCEEDED)
            return
        }
        val generation = ++requestGeneration
        val operationId = pendingOperationId ?: operationIdFactory().also { pendingOperationId = it }
        requestJob?.cancel()
        mutableState.value = snapshot.copy(phase = TextEditorPhase.SAVING, errorCode = null, requestId = null)
        requestJob =
            viewModelScope.launch {
                try {
                    val latestFile = files.detail(fileId)
                    if (!canEditText(latestFile.permission, latestFile.permissionSource)) {
                        throw KuraStorageException.Api(ApiErrorFactory.forbidden())
                    }
                    val result = text.save(fileId, snapshot.draft, base.fileVersion, operationId)
                    if (generation != requestGeneration) return@launch
                    val updated = TextDocument(snapshot.draft, "UTF-8", result.fileVersion, result.size, result.sha256)
                    pendingOperationId = null
                    clearSavedDraft()
                    mutableState.value =
                        snapshot.copy(
                            file = latestFile.copy(fileVersion = result.fileVersion, size = result.size),
                            document = updated,
                            draft = updated.content,
                            phase = TextEditorPhase.SAVED,
                            dirty = false,
                            canEdit = true,
                            conflict = null,
                            diff = emptyList(),
                            diffTruncated = false,
                            conflictReloadFailed = false,
                        )
                } catch (error: Throwable) {
                    if (generation != requestGeneration) return@launch
                    if (error.isVersionConflict()) {
                        loadConflict(generation, snapshot, base.fileVersion)
                    } else {
                        mutableState.value = snapshot.mergeError(error)
                    }
                }
            }
    }

    fun saveAndExit() {
        val current = mutableState.value
        if (!current.dirty || !current.canEdit) return
        mutableState.value = current.copy(showDiscardConfirmation = false, exitAfterSave = true)
        save()
    }

    fun consumeExitAfterSave() {
        mutableState.value = mutableState.value.copy(exitAfterSave = false)
    }

    fun reloadAfterConflict() {
        val current = mutableState.value
        val latest = current.conflict?.current ?: return
        pendingOperationId = null
        clearSavedDraft()
        mutableState.value =
            current.copy(
                document = latest,
                draft = latest.content,
                phase = TextEditorPhase.VIEWING,
                dirty = false,
                conflict = null,
                diff = emptyList(),
                diffTruncated = false,
                conflictReloadFailed = false,
            )
    }

    fun requestExit(): Boolean {
        val dirty = mutableState.value.dirty
        if (dirty) mutableState.value = mutableState.value.copy(showDiscardConfirmation = true)
        return dirty
    }

    fun dismissDiscardConfirmation() {
        mutableState.value = mutableState.value.copy(showDiscardConfirmation = false)
    }

    fun discardChanges() {
        clearSavedDraft()
        mutableState.value = mutableState.value.copy(dirty = false, showDiscardConfirmation = false)
    }

    private suspend fun loadConflict(
        generation: Long,
        snapshot: TextEditorUiState,
        expectedVersion: Long,
    ) {
        runCatching { text.current(fileId) }
            .onSuccess { current ->
                if (generation != requestGeneration) return@onSuccess
                val conflict = TextConflict(snapshot.draft, expectedVersion, current)
                mutableState.value =
                    snapshot.copy(
                        phase = TextEditorPhase.CONFLICT,
                        conflict = conflict,
                        diff = BoundedLineDiff.compare(current.content, snapshot.draft),
                        diffTruncated = BoundedLineDiff.isTruncated(current.content, snapshot.draft),
                        conflictReloadFailed = false,
                        errorCode = ErrorCode.FILE_VERSION_CONFLICT,
                    )
            }.onFailure { error ->
                if (generation == requestGeneration) {
                    mutableState.value = snapshot.mergeError(error).copy(conflictReloadFailed = true)
                }
            }
    }

    private fun restoredDraft(document: TextDocument): String? =
        savedState
            .get<String>(KEY_FILE_ID)
            ?.takeIf { it == fileId }
            ?.let { savedState.get<Long>(KEY_BASE_VERSION)?.takeIf { version -> version == document.fileVersion } }
            ?.let { savedState.get<String>(KEY_DRAFT) }

    private fun persistDraft(
        value: String,
        baseVersion: Long?,
    ): Boolean {
        if (SupportedTextMimeTypes.encodedSize(value) > MAX_SAVED_DRAFT_BYTES || baseVersion == null) {
            clearSavedDraft()
            return false
        }
        savedState[KEY_FILE_ID] = fileId
        savedState[KEY_BASE_VERSION] = baseVersion
        savedState[KEY_DRAFT] = value
        return true
    }

    private fun clearSavedDraft() {
        savedState.remove<String>(KEY_FILE_ID)
        savedState.remove<Long>(KEY_BASE_VERSION)
        savedState.remove<String>(KEY_DRAFT)
    }

    private fun errorState(error: Throwable) = TextEditorUiState(phase = TextEditorPhase.ERROR).mergeError(error)

    private fun TextEditorUiState.mergeError(error: Throwable): TextEditorUiState {
        val api = error as? KuraStorageException.Api
        val code = api?.error?.code ?: ErrorCode.UNKNOWN
        return copy(
            phase = TextEditorPhase.ERROR,
            canEdit = canEdit && code != ErrorCode.FILE_NOT_FOUND,
            errorCode = code,
            requestId = api?.error?.requestId,
        )
    }

    private fun Throwable.isVersionConflict() = (this as? KuraStorageException.Api)?.error?.code == ErrorCode.FILE_VERSION_CONFLICT

    private fun FileEntry.isSupportedText() =
        entryType == FileEntryType.FILE && status == FileEntryStatus.ACTIVE && SupportedTextMimeTypes.isSupported(mimeType)

    companion object {
        const val MAX_SAVED_DRAFT_BYTES = 64 * 1024
        private const val KEY_FILE_ID = "text.fileId"
        private const val KEY_BASE_VERSION = "text.baseVersion"
        private const val KEY_DRAFT = "text.draft"
        private val EDITABLE_PHASES =
            setOf(
                TextEditorPhase.VIEWING,
                TextEditorPhase.EDITING,
                TextEditorPhase.SAVED,
                TextEditorPhase.CONFLICT,
                TextEditorPhase.ERROR,
            )
    }
}

private object ApiErrorFactory {
    fun forbidden() =
        com.kurastorage.core.model
            .ApiError(ErrorCode.FILE_NOT_FOUND, null, 403)
}
