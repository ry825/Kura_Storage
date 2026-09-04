@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.core.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.kurastorage.core.ui.state.KuraStateKind
import com.kurastorage.core.ui.state.KuraStateView

@Composable
fun KuraStorageTheme(
    darkTheme: Boolean = androidx.compose.foundation.isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    KuraMaterialTheme(darkTheme = darkTheme, content = content)
}

@Composable
fun LoadingState(label: String) {
    CenteredState {
        KuraStateView(
            kind = KuraStateKind.LOADING,
            title = label,
            message = "Please wait.",
        )
    }
}

@Composable
fun EmptyState(message: String) {
    CenteredState {
        KuraStateView(
            kind = KuraStateKind.EMPTY,
            title = "Nothing to show",
            message = message,
        )
    }
}

@Composable
fun ErrorState(
    message: String,
    requestId: String? = null,
    onRetry: (() -> Unit)? = null,
) {
    CenteredState {
        KuraStateView(
            kind = if (onRetry == null) KuraStateKind.BLOCKING_ERROR else KuraStateKind.RECOVERABLE_ERROR,
            title = "Unable to continue",
            message = message,
            requestId = requestId,
            actionLabel = if (onRetry == null) null else "Try again",
            onAction = onRetry,
        )
    }
}

@Composable
fun ProgressState(
    label: String,
    progress: Float?,
) {
    CenteredState {
        KuraStateView(
            kind = KuraStateKind.PROGRESS,
            title = label,
            message = "In progress",
            progress = progress,
        )
    }
}

@Composable
private fun CenteredState(content: @Composable () -> Unit) {
    Surface(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier.padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
            horizontalAlignment = Alignment.CenterHorizontally,
            content = { content() },
        )
    }
}

enum class AppDestination(
    val route: String,
) {
    CONNECTION("connection"),
    AUTHENTICATION("authentication"),
    HOME("home"),
    FILES("files"),
    SETTINGS("settings"),
    TRASH("trash"),
    SHARING("sharing"),
    SHARING_SETTINGS("sharing-settings"),
    SEARCH("search"),
    RECENT_FILES("recent-files"),
    ACTIVITY("activity"),
    FAVORITES("favorites"),
    TAGS("tags"),
    ENTRY_ORGANIZATION("entry-organization"),
    MEDIA_SETTINGS("media-settings"),
    CACHE_MANAGEMENT("cache-management"),
    BACKUP_SETTINGS("backup-settings"),
    BACKUP_OVERVIEW("backup-overview"),
    BACKUP_RULES("backup-rules"),
    BACKUP_WIFI("backup-wifi"),
    BACKUP_DESTINATION("backup-destination"),
    PHOTO_VIEWER("media/photo"),
    PDF_VIEWER("media/pdf"),
    VIDEO_PLAYER("media/video"),
    AUDIO_PLAYER("media/audio"),
    TEXT_EDITOR("text/editor"),
    TEXT_HISTORY("text/history"),
}
