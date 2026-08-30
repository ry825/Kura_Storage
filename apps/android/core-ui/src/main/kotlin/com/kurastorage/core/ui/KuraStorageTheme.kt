@file:Suppress("ktlint:standard:function-naming", "FunctionNaming")

package com.kurastorage.core.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

@Composable
fun KuraStorageTheme(content: @Composable () -> Unit) {
    MaterialTheme(content = content)
}

@Composable
fun LoadingState(label: String) {
    CenteredState {
        CircularProgressIndicator()
        Text(label)
    }
}

@Composable
fun EmptyState(message: String) {
    CenteredState { Text(message) }
}

@Composable
fun ErrorState(
    message: String,
    requestId: String? = null,
    onRetry: (() -> Unit)? = null,
) {
    CenteredState {
        Text(message, color = MaterialTheme.colorScheme.error)
        requestId?.let { Text("Request ID: $it", style = MaterialTheme.typography.bodySmall) }
        onRetry?.let { action ->
            Button(onClick = action) { Text("Try again") }
        }
    }
}

@Composable
fun ProgressState(
    label: String,
    progress: Float?,
) {
    CenteredState {
        if (progress == null) {
            CircularProgressIndicator()
        } else {
            CircularProgressIndicator(progress = { progress.coerceIn(0f, 1f) })
        }
        Text(label)
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
    TRASH("trash"),
    SHARING("sharing"),
    SHARING_SETTINGS("sharing-settings"),
    SEARCH("search"),
    RECENT_FILES("recent-files"),
    FAVORITES("favorites"),
    TAGS("tags"),
    ENTRY_ORGANIZATION("entry-organization"),
    MEDIA_SETTINGS("media-settings"),
    PHOTO_VIEWER("media/photo"),
    PDF_VIEWER("media/pdf"),
}
