package com.kurastorage.feature.media

import android.graphics.Bitmap
import android.util.Base64
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertHeightIsAtLeast
import androidx.compose.ui.test.assertHeightIsEqualTo
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertWidthIsAtLeast
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeLeft
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import coil3.ImageLoader
import coil3.decode.DataSource
import coil3.decode.ImageSource
import coil3.fetch.Fetcher
import coil3.fetch.SourceFetchResult
import com.kurastorage.core.data.media.KuraMediaImage
import com.kurastorage.core.data.media.KuraMediaKeyer
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.TagItem
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.OriginalMetadata
import com.kurastorage.core.model.media.ReadyMediaSource
import com.kurastorage.feature.media.pdf.PdfFailure
import com.kurastorage.feature.media.pdf.PdfLoadState
import com.kurastorage.feature.media.pdf.PdfViewerScreen
import com.kurastorage.feature.media.pdf.PdfViewerUiState
import com.kurastorage.feature.media.photo.PhotoDownloadStatus
import com.kurastorage.feature.media.photo.PhotoDownloadUiState
import com.kurastorage.feature.media.photo.PhotoOrganizationUiState
import com.kurastorage.feature.media.photo.PhotoViewerScreen
import com.kurastorage.feature.media.photo.PhotoViewerUiState
import com.kurastorage.feature.media.thumbnail.FileThumbnail
import kotlinx.coroutines.delay
import okio.Buffer
import okio.FileSystem
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class MediaViewerScreenTest {
    @get:Rule val compose = createComposeRule()

    private var loader: ImageLoader? = null

    @After
    fun closeLoader() {
        loader?.shutdown()
    }

    @Test
    fun thumbnailShowsReadyAndMissingSemantics() {
        loader = imageLoader()
        val ready = file("photo", "image/png")
        val missing = file("missing", "image/png").copy(status = FileEntryStatus.MISSING)
        compose.setContent {
            androidx.compose.foundation.layout.Column {
                FileThumbnail(ready, "scope", checkNotNull(loader), Modifier.size(96.dp))
                FileThumbnail(missing, "scope", checkNotNull(loader), Modifier.size(96.dp))
            }
        }

        compose.onNodeWithContentDescription("Missing file: missing").assertIsDisplayed()
        compose.waitUntil(5_000) {
            runCatching {
                compose.onNodeWithContentDescription("Thumbnail: photo").assertIsDisplayed()
            }.isSuccess
        }
        compose.onNodeWithContentDescription("Thumbnail: photo").assertIsDisplayed()
    }

    @Test
    fun thumbnailShowsPlaceholderWhileRequestIsPending() {
        loader = imageLoader(delayMillis = 30_000)
        compose.setContent { FileThumbnail(file("pending", "image/png"), "scope-pending", checkNotNull(loader)) }

        compose.onNodeWithContentDescription("Loading thumbnail: pending").assertIsDisplayed()
    }

    @Test
    fun thumbnailShowsActionableErrorSemantics() {
        loader = imageLoader(throwError = true)
        compose.setContent { FileThumbnail(file("broken", "image/png"), "scope-error", checkNotNull(loader)) }

        compose.waitUntil(5_000) {
            runCatching {
                compose.onNodeWithContentDescription("Thumbnail unavailable: broken").assertIsDisplayed()
            }.isSuccess
        }
        compose.onNodeWithContentDescription("Thumbnail unavailable: broken").assertIsDisplayed()
    }

    @Test
    fun photoLoadsOriginalWithoutConfirmationAndExposesActualQuality() {
        val photo = file("photo", "image/jpeg")
        var state by
            mutableStateOf(
                PhotoViewerUiState(
                    file = photo,
                    media =
                        MediaViewerState(
                            photo.id,
                            photo.fileVersion,
                            MediaKind.IMAGE,
                            MediaQuality.ORIGINAL,
                            NetworkQualityContext.REMOTE_MOBILE,
                            MediaLoadState.Loading,
                            originalSizeLabel = "1 KB",
                        ),
                    currentPosition = 3,
                    totalCount = 24,
                ),
            )
        loader = ImageLoader.Builder(InstrumentationRegistry.getInstrumentation().targetContext).build()
        compose.setContent {
            PhotoViewerScreen(
                state,
                checkNotNull(loader),
                "scope",
                requestTicket = { null },
                onImageReady = {},
                onGenerating = { _, _ -> },
                onImageFailed = {},
                onQuality = {},
                onPrevious = {},
                onNext = {},
                onZoom = { state = state.copy(zoom = it) },
                onDetails = {},
                onDownloadOriginal = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Load original photo?").assertDoesNotExist()
        compose.onNodeWithText("3 / 24").assertIsDisplayed()
        compose.onNodeWithText("Loading: Original").performScrollTo().assertIsDisplayed()
        compose.onNodeWithContentDescription("Zoom in").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(1.5f, state.zoom) }
    }

    @Test
    fun pdfExposesPageNavigationAndSafeError() {
        var next = false
        compose.setContent {
            PdfViewerScreen(
                PdfViewerUiState(
                    file = file("report", "application/pdf"),
                    loadState = PdfLoadState.FAILED,
                    bitmap = Bitmap.createBitmap(1, 1, Bitmap.Config.ARGB_8888),
                    pageIndex = 1,
                    pageCount = 3,
                    failure = PdfFailure.UNKNOWN,
                ),
                onConfirm = {},
                onPrevious = {},
                onNext = { next = true },
                onPage = {},
                onZoom = {},
                onViewport = { _, _ -> },
                onRetryOpen = {},
                onSaveCopy = {},
                onBack = {},
                onDisposeViewer = {},
            )
        }

        compose.onNodeWithText("Page 2 / 3 • Zoom 1.0x").assertIsDisplayed()
        compose.onNodeWithText("This PDF could not be opened safely.").assertIsDisplayed()
        compose.onNodeWithText("Next").performClick()
        compose.runOnIdle { assertEquals(true, next) }
    }

    @Test
    fun pdfConfirmationShowsMimeRangeAndEstimatedTransferBeforeContent() {
        var confirmed = false
        compose.setContent {
            PdfViewerScreen(
                PdfViewerUiState(
                    file = file("report", "application/pdf"),
                    metadata = OriginalMetadata(ByteCount(2_048), "application/pdf", true),
                    loadState = PdfLoadState.CONFIRMING,
                ),
                onConfirm = { confirmed = true },
                onPrevious = {},
                onNext = {},
                onPage = {},
                onZoom = {},
                onViewport = { _, _ -> },
                onRetryOpen = {},
                onSaveCopy = {},
                onBack = {},
                onDisposeViewer = {},
            )
        }

        compose.onNodeWithText("MIME: application/pdf", substring = true).assertIsDisplayed()
        compose.onNodeWithText("Estimated transfer: 2 KB", substring = true).assertIsDisplayed()
        compose.onNodeWithText("Range support: Yes", substring = true).assertIsDisplayed()
        compose.onNodeWithText("Open PDF").performClick()
        compose.runOnIdle { assertEquals(true, confirmed) }
    }

    @Test
    fun longPdfNameKeepsHeaderActionAndPageNavigationOnScreen() {
        compose.setContent {
            PdfViewerScreen(
                PdfViewerUiState(
                    file = file("a-very-long-file-name-that-must-not-collapse-viewer-actions.pdf", "application/pdf"),
                    loadState = PdfLoadState.READY,
                    bitmap = Bitmap.createBitmap(1, 1, Bitmap.Config.ARGB_8888),
                    pageIndex = 0,
                    pageCount = 3,
                ),
                onConfirm = {},
                onPrevious = {},
                onNext = {},
                onPage = {},
                onZoom = {},
                onViewport = { _, _ -> },
                onRetryOpen = {},
                onSaveCopy = {},
                onBack = {},
                onDisposeViewer = {},
            )
        }

        compose.onNodeWithText("Save a copy").assertIsDisplayed().assertHeightIsEqualTo(48.dp)
        compose.onNodeWithText("Next").assertIsDisplayed()
    }

    @Test
    fun darkThemeLargeTextKeepsPrimaryPhotoOperationsReachable() {
        val photo = file("accessible", "image/jpeg")
        loader = imageLoader()
        val media =
            MediaViewerState(
                fileId = photo.id,
                fileVersion = photo.fileVersion,
                kind = MediaKind.IMAGE,
                quality = MediaQuality.LOW,
                networkContext = NetworkQualityContext.REMOTE_MOBILE,
                loadState =
                    MediaLoadState.Ready(
                        ReadyMediaSource(photo.id, photo.fileVersion, MediaVariant.IMAGE_LOW),
                    ),
            )
        val density =
            InstrumentationRegistry
                .getInstrumentation()
                .targetContext.resources.displayMetrics.density
        compose.setContent {
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale = 2f)) {
                MaterialTheme(colorScheme = darkColorScheme()) {
                    Box(Modifier.requiredSize(360.dp, 640.dp)) {
                        PhotoViewerScreen(
                            state = PhotoViewerUiState(file = photo, media = media, canGoNext = true),
                            imageLoader = checkNotNull(loader),
                            scopeId = "accessibility",
                            requestTicket = { null },
                            onImageReady = {},
                            onGenerating = { _, _ -> },
                            onImageFailed = {},
                            onQuality = {},
                            onPrevious = {},
                            onNext = {},
                            onZoom = {},
                            onDetails = {},
                            onDownloadOriginal = {},
                            onBack = {},
                        )
                    }
                }
            }
        }

        compose
            .onNodeWithContentDescription("Back")
            .assertWidthIsAtLeast(48.dp)
            .assertHeightIsAtLeast(48.dp)
        compose
            .onNodeWithContentDescription("Download original")
            .performScrollTo()
            .assertIsDisplayed()
            .assertIsEnabled()
        compose
            .onNodeWithContentDescription("Next photo")
            .performScrollTo()
            .assertIsDisplayed()
            .assertIsEnabled()
        compose.onNodeWithContentDescription("Full screen").performScrollTo().performClick()
        compose.onNodeWithTag("photo-fullscreen").assertIsDisplayed()
        compose.onNodeWithContentDescription("Exit full screen").assertIsDisplayed().performClick()
        compose.onNodeWithTag("photo-fullscreen").assertDoesNotExist()
    }

    @Test
    fun photoSwipeMovesOnceAtActualSizeAndDoesNotNavigateWhileZoomed() {
        val photo = file("swipe", "image/jpeg")
        var nextCount = 0
        var state by mutableStateOf(photoState(photo).copy(canGoNext = true))
        loader = imageLoader()
        compose.setContent {
            PhotoViewerScreen(
                state = state,
                imageLoader = checkNotNull(loader),
                scopeId = "swipe-scope",
                requestTicket = { null },
                onImageReady = {},
                onGenerating = { _, _ -> },
                onImageFailed = {},
                onQuality = {},
                onPrevious = {},
                onNext = { nextCount++ },
                onZoom = { state = state.copy(zoom = it) },
                onDetails = {},
                onDownloadOriginal = {},
                onBack = {},
            )
        }

        compose.onNodeWithTag("photo-canvas").performTouchInput { swipeLeft(durationMillis = 300) }
        compose.runOnIdle { assertEquals(1, nextCount) }
        compose.runOnIdle { state = state.copy(zoom = 2f) }
        compose.onNodeWithTag("photo-canvas").performTouchInput { swipeLeft(durationMillis = 300) }
        compose.runOnIdle { assertEquals(1, nextCount) }
    }

    @Test
    fun photoToolbarAndTagSheetExposeServerBackedOrganizationActions() {
        val photo = file("organized", "image/jpeg")
        val travel = TagItem("travel", "A long travel tag that remains scrollable at large text")
        var favoriteToggles = 0
        var selectedTag: TagItem? = null
        var refreshes = 0
        loader = imageLoader()
        compose.setContent {
            PhotoViewerScreen(
                state = photoState(photo),
                imageLoader = checkNotNull(loader),
                scopeId = "organization-scope",
                requestTicket = { null },
                onImageReady = {},
                onGenerating = { _, _ -> },
                onImageFailed = {},
                onQuality = {},
                onPrevious = {},
                onNext = {},
                onZoom = {},
                onDetails = {},
                onDownloadOriginal = {},
                onBack = {},
                organization =
                    PhotoOrganizationUiState(
                        isFavorite = true,
                        attachedTags = listOf(travel),
                        availableTags = listOf(travel),
                        canAttach = true,
                        loading = false,
                    ),
                onRefreshOrganization = { refreshes++ },
                onToggleFavorite = { favoriteToggles++ },
                onToggleTag = { selectedTag = it },
            )
        }

        compose.onNodeWithContentDescription("Remove from favorites").performScrollTo().performClick()
        compose.onNodeWithContentDescription("Manage photo tags").performScrollTo().performClick()
        compose.onNodeWithTag("photo-tags-sheet").assertIsDisplayed()
        compose.onNodeWithTag("photo-tag-travel").performScrollTo().performClick()
        compose.onNodeWithContentDescription("Refresh photo tags").performScrollTo().performClick()
        compose.runOnIdle {
            assertEquals(1, favoriteToggles)
            assertEquals(travel, selectedTag)
            assertEquals(1, refreshes)
        }
    }

    @Test
    fun photoToolbarDisablesPendingFavoriteAndReportsIncompleteDownload() {
        val photo = file("pending-actions", "image/jpeg")
        var quality: MediaQuality? = null
        loader = imageLoader()
        compose.setContent {
            PhotoViewerScreen(
                state = photoState(photo),
                imageLoader = checkNotNull(loader),
                scopeId = "pending-actions-scope",
                requestTicket = { null },
                onImageReady = {},
                onGenerating = { _, _ -> },
                onImageFailed = {},
                onQuality = { quality = it },
                onPrevious = {},
                onNext = {},
                onZoom = {},
                onDetails = {},
                onDownloadOriginal = {},
                onBack = {},
                organization =
                    PhotoOrganizationUiState(
                        isFavorite = true,
                        pendingFavorite = true,
                        canAttach = true,
                        loading = false,
                    ),
                download = PhotoDownloadUiState(PhotoDownloadStatus.INCOMPLETE_FILE_MAY_REMAIN),
            )
        }

        compose.onNodeWithContentDescription("Saving favorite").performScrollTo().assertIsNotEnabled()
        compose
            .onNodeWithText("The incomplete destination could not be removed. Delete it before retrying.")
            .performScrollTo()
            .assertIsDisplayed()
        compose.onNodeWithText("Medium").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(MediaQuality.MEDIUM, quality) }
    }

    @Test
    fun previousAndNextControlsAreOutsidePhotoViewport() {
        val photo = file("bounds", "image/jpeg")
        loader = imageLoader()
        compose.setContent {
            PhotoViewerScreen(
                state = photoState(photo).copy(canGoPrevious = true, canGoNext = true),
                imageLoader = checkNotNull(loader),
                scopeId = "bounds-scope",
                requestTicket = { null },
                onImageReady = {},
                onGenerating = { _, _ -> },
                onImageFailed = {},
                onQuality = {},
                onPrevious = {},
                onNext = {},
                onZoom = {},
                onDetails = {},
                onDownloadOriginal = {},
                onBack = {},
            )
        }

        val viewport = compose.onNodeWithTag("photo-viewport").fetchSemanticsNode().boundsInRoot
        val previous = compose.onNodeWithContentDescription("Previous photo").fetchSemanticsNode().boundsInRoot
        val next = compose.onNodeWithContentDescription("Next photo").fetchSemanticsNode().boundsInRoot
        assertTrue(!viewport.overlaps(previous))
        assertTrue(!viewport.overlaps(next))
    }

    private fun photoState(photo: FileEntry): PhotoViewerUiState =
        PhotoViewerUiState(
            file = photo,
            media =
                MediaViewerState(
                    fileId = photo.id,
                    fileVersion = photo.fileVersion,
                    kind = MediaKind.IMAGE,
                    quality = MediaQuality.LOW,
                    networkContext = NetworkQualityContext.REMOTE_MOBILE,
                    loadState = MediaLoadState.Ready(ReadyMediaSource(photo.id, photo.fileVersion, MediaVariant.IMAGE_LOW)),
                    originalSizeLabel = "1 KB",
                ),
        )

    private fun imageLoader(
        throwError: Boolean = false,
        delayMillis: Long = 0,
    ): ImageLoader {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        return ImageLoader
            .Builder(context)
            .components {
                add(KuraMediaKeyer, KuraMediaImage::class)
                add(
                    Fetcher.Factory<KuraMediaImage> { _, _, _ ->
                        Fetcher {
                            if (delayMillis > 0) delay(delayMillis)
                            if (throwError) error("decode failed")
                            SourceFetchResult(
                                ImageSource(Buffer().write(PNG_BYTES), FileSystem.SYSTEM),
                                "image/png",
                                DataSource.NETWORK,
                            )
                        }
                    },
                    KuraMediaImage::class,
                )
            }.build()
    }

    private fun file(
        name: String,
        mime: String,
    ) = FileEntry(
        id = "$name-id",
        parentId = null,
        name = name,
        entryType = FileEntryType.FILE,
        mimeType = mime,
        size = 1_024,
        status = FileEntryStatus.ACTIVE,
        fileVersion = 1,
        trashedAt = null,
        createdAt = Instant.EPOCH,
        updatedAt = Instant.EPOCH,
        owner = OwnerSummary("owner", "Owner"),
        permission = SharePermission.MANAGER,
        permissionSource = PermissionSource.OWNER,
    )

    private companion object {
        val PNG_BYTES: ByteArray =
            Base64.decode(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=",
                Base64.DEFAULT,
            )
    }
}
