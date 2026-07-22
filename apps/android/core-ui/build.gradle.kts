plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.compose")
    id("kurastorage.android.test")
}

android.namespace = "com.kurastorage.core.ui"

dependencies {
    implementation(project(":core-model"))
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.ui.tooling.preview)
}
