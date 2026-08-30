package com.kurastorage.feature.media

import android.graphics.Bitmap
import android.util.Base64
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
import androidx.compose.ui.test.assertWidthIsAtLeast
import androidx.compose.ui.test.doubleClick
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTouchInput
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
import com.kurastorage.core.data.media.TransferConfirmationPrompt
import com.kurastorage.core.model.FileEntry
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SharePermission
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaLoadState
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.ReadyMediaSource
import com.kurastorage.feature.media.pdf.PdfLoadState
import com.kurastorage.feature.media.pdf.PdfViewerScreen
import com.kurastorage.feature.media.pdf.PdfViewerUiState
import com.kurastorage.feature.media.photo.PhotoViewerScreen
import com.kurastorage.feature.media.photo.PhotoViewerUiState
import com.kurastorage.feature.media.thumbnail.FileThumbnail
import kotlinx.coroutines.delay
import okio.Buffer
import okio.FileSystem
import org.junit.After
import org.junit.Assert.assertEquals
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
    fun photoRequiresOriginalConfirmationAndDoubleTapChangesZoom() {
        val photo = file("photo", "image/jpeg")
        val prompt =
            TransferConfirmationPrompt(
                photo.id,
                photo.fileVersion,
                MediaKind.IMAGE,
                MediaVariant.ORIGINAL,
                ByteCount(1_024),
                true,
                "1.0 KiB",
                "About 1.0 KiB may be transferred.",
            )
        var confirmed = false
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
                            MediaLoadState.ConfirmingTransfer,
                            prompt,
                        ),
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
                onConfirmOriginal = { confirmed = true },
                onPrevious = {},
                onNext = {},
                onZoom = { state = state.copy(zoom = it) },
                onDetails = {},
                onDownload = {},
                onBack = {},
            )
        }

        compose.onNodeWithText("Load original photo?").assertIsDisplayed()
        compose.onNodeWithText("Load original").performClick()
        compose.runOnIdle { assertEquals(true, confirmed) }
        compose.onNodeWithTag("photo-canvas").performTouchInput { doubleClick() }
        compose.runOnIdle { assertEquals(2f, state.zoom) }
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
                    error = "This PDF could not be opened safely.",
                ),
                onConfirm = {},
                onPrevious = {},
                onNext = { next = true },
                onPage = {},
                onZoom = {},
                onViewport = { _, _ -> },
                onDownload = {},
                onBack = {},
                onDisposeViewer = {},
            )
        }

        compose.onNodeWithText("Page 2 / 3 • Zoom 1.0x").assertIsDisplayed()
        compose.onNodeWithText("This PDF could not be opened safely.").assertIsDisplayed()
        compose.onNodeWithText("Next page").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(true, next) }
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
                onDownload = {},
                onBack = {},
                onDisposeViewer = {},
            )
        }

        compose.onNodeWithText("Download").assertIsDisplayed().assertHeightIsEqualTo(48.dp)
        compose.onNodeWithText("Next page").assertIsDisplayed()
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
                    PhotoViewerScreen(
                        state = PhotoViewerUiState(file = photo, media = media, canGoNext = true),
                        imageLoader = checkNotNull(loader),
                        scopeId = "accessibility",
                        requestTicket = { null },
                        onImageReady = {},
                        onGenerating = { _, _ -> },
                        onImageFailed = {},
                        onQuality = {},
                        onConfirmOriginal = {},
                        onPrevious = {},
                        onNext = {},
                        onZoom = {},
                        onDetails = {},
                        onDownload = {},
                        onBack = {},
                    )
                }
            }
        }

        compose
            .onNodeWithText("Back")
            .assertWidthIsAtLeast(48.dp)
            .assertHeightIsAtLeast(48.dp)
        compose
            .onNodeWithText("Download this quality")
            .performScrollTo()
            .assertIsDisplayed()
            .assertIsEnabled()
        compose
            .onNodeWithText("Next photo")
            .performScrollTo()
            .assertIsDisplayed()
            .assertIsEnabled()
    }

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
