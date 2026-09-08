package com.kurastorage.core.model.media

data class QualityPreferences(
    val localDirect: PhotoDisplayMode = PhotoDisplayMode.ORIGINAL,
    val registeredRemoteWifi: PhotoDisplayMode = PhotoDisplayMode.FAST,
    val unregisteredRemoteWifi: PhotoDisplayMode = PhotoDisplayMode.FAST,
    val remoteMobile: PhotoDisplayMode = PhotoDisplayMode.FAST,
) {
    fun qualityFor(context: NetworkQualityContext): PhotoDisplayMode =
        when (context) {
            NetworkQualityContext.LOCAL_DIRECT -> localDirect
            NetworkQualityContext.REGISTERED_REMOTE_WIFI -> registeredRemoteWifi
            NetworkQualityContext.UNREGISTERED_REMOTE_WIFI -> unregisteredRemoteWifi
            NetworkQualityContext.REMOTE_MOBILE -> remoteMobile
        }
}
