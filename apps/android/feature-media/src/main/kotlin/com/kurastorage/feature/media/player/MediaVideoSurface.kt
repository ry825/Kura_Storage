@file:Suppress("FunctionNaming", "ktlint:standard:function-naming")

package com.kurastorage.feature.media.player

import androidx.annotation.OptIn
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.media3.common.util.UnstableApi
import androidx.media3.ui.compose.PlayerSurface

@OptIn(UnstableApi::class)
@Composable
fun MediaVideoSurface(
    controller: AndroidMediaPlayerController,
    modifier: Modifier = Modifier,
) {
    PlayerSurface(player = controller.player, modifier = modifier)
}
