plugins {
    id("kurastorage.android.application")
    id("kurastorage.android.compose")
    id("kurastorage.android.test")
}

android {
    namespace = "com.kurastorage.app"
    defaultConfig {
        applicationId = "com.kurastorage.app"
        versionCode = 1
        versionName = "0.1.0"
    }
}

dependencies {
    implementation(project(":core-ui"))
    implementation(project(":feature-connection"))
    implementation(project(":feature-auth"))
    implementation(project(":feature-files"))
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
