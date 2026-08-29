package com.kurastorage.feature.settings

import com.kurastorage.core.data.media.QualityPreferenceStore
import com.kurastorage.core.model.media.MediaQuality
import com.kurastorage.core.model.media.NetworkQualityContext
import com.kurastorage.core.model.media.QualityPreferences
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class QualitySettingsViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before
    fun setUp() = Dispatchers.setMain(dispatcher)

    @After
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `loads defaults and persists one context without changing other contexts`() =
        runTest(dispatcher) {
            val store = FakeStore()
            val viewModel = QualitySettingsViewModel(store)
            dispatcher.scheduler.advanceUntilIdle()

            viewModel.update(NetworkQualityContext.REMOTE_MOBILE, MediaQuality.MEDIUM)
            dispatcher.scheduler.advanceUntilIdle()

            assertEquals(MediaQuality.MEDIUM, viewModel.state.value.preferences.remoteMobile)
            assertEquals(MediaQuality.ORIGINAL, viewModel.state.value.preferences.localDirect)
            assertEquals(NetworkQualityContext.REMOTE_MOBILE to MediaQuality.MEDIUM, store.updated)
        }

    private class FakeStore : QualityPreferenceStore {
        var updated: Pair<NetworkQualityContext, MediaQuality>? = null

        override suspend fun read() = QualityPreferences()

        override suspend fun update(
            context: NetworkQualityContext,
            quality: MediaQuality,
        ) {
            updated = context to quality
        }
    }
}
