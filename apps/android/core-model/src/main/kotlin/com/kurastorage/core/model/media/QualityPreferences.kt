package com.kurastorage.core.model.media

data class QualityPreferences(
    val localDirect: MediaQuality = MediaQuality.ORIGINAL,
    val registeredRemoteWifi: MediaQuality = MediaQuality.MEDIUM,
    val unregisteredRemoteWifi: MediaQuality = MediaQuality.LOW,
    val remoteMobile: MediaQuality = MediaQuality.LOW,
) {
    fun qualityFor(context: NetworkQualityContext): MediaQuality =
        when (context) {
            NetworkQualityContext.LOCAL_DIRECT -> localDirect
            NetworkQualityContext.REGISTERED_REMOTE_WIFI -> registeredRemoteWifi
            NetworkQualityContext.UNREGISTERED_REMOTE_WIFI -> unregisteredRemoteWifi
            NetworkQualityContext.REMOTE_MOBILE -> remoteMobile
        }
}
