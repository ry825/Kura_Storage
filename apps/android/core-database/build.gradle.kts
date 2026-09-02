plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
    alias(libs.plugins.ksp)
}

android {
    namespace = "com.kurastorage.core.database"
    sourceSets["androidTest"].assets.srcDir("schemas")
    defaultConfig {
        ksp {
            arg("room.schemaLocation", file("schemas").path)
            arg("room.incremental", "true")
            arg("room.generateKotlin", "true")
        }
    }
}

dependencies {
    implementation(project(":core-model"))
    implementation(libs.androidx.room.runtime)
    implementation(libs.androidx.room.ktx)
    implementation(libs.kotlinx.coroutines.android)
    ksp(libs.androidx.room.compiler)
    testImplementation(libs.kotlinx.coroutines.test)
    androidTestImplementation(libs.androidx.room.testing)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.kotlinx.coroutines.test)
}
