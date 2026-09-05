package com.kurastorage.feature.media.thumbnail

import androidx.compose.foundation.layout.size
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import coil3.ImageLoader
import com.kurastorage.core.model.FileEntryStatus
import com.kurastorage.core.model.FileEntryType
import com.kurastorage.core.model.OwnerSummary
import com.kurastorage.core.model.PermissionSource
import com.kurastorage.core.model.SearchFileCategory
import com.kurastorage.core.model.SearchResultItem
import com.kurastorage.core.model.SharePermission
import org.junit.Rule
import org.junit.Test
import java.time.Instant

class FileThumbnailTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun favoriteMetadataUsesTypeFallbacksForNonThumbnailEntries() {
        val loader = ImageLoader.Builder(InstrumentationRegistry.getInstrumentation().targetContext).build()
        compose.setContent {
            FileThumbnail(metadata("notes.txt", "text/plain"), "scope", loader, Modifier.size(72.dp))
        }

        compose.onNodeWithContentDescription("File: notes.txt").assertIsDisplayed()
    }

    @Test
    fun failedFavoriteThumbnailFallsBackWithoutReplacingTheEntry() {
        val loader = ImageLoader.Builder(InstrumentationRegistry.getInstrumentation().targetContext).build()
        compose.setContent {
            FileThumbnail(metadata("photo.jpg", "image/jpeg"), "scope", loader, Modifier.size(72.dp))
        }

        compose.waitUntil(timeoutMillis = 5_000) {
            compose.onAllNodes(hasContentDescription("Thumbnail unavailable: photo.jpg")).fetchSemanticsNodes().isNotEmpty()
        }
        compose.onNodeWithContentDescription("Thumbnail unavailable: photo.jpg").assertIsDisplayed()
    }

    private fun metadata(
        name: String,
        mimeType: String,
    ) = SearchResultItem(
        id = name,
        entryType = FileEntryType.FILE,
        name = name,
        mimeType = mimeType,
        fileCategory = SearchFileCategory.OTHER,
        size = 1,
        status = FileEntryStatus.ACTIVE,
        updatedAt = Instant.parse("2026-09-05T00:00:00Z"),
        owner = OwnerSummary("owner", "Owner"),
        permission = SharePermission.MANAGER,
        permissionSource = PermissionSource.OWNER,
        shareTargetId = null,
    )
}
