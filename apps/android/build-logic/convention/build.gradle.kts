plugins {
    `kotlin-dsl`
}

group = "com.kurastorage.buildlogic"

dependencies {
    implementation("com.android.tools.build:gradle:8.13.2")
    implementation("org.jetbrains.kotlin:kotlin-gradle-plugin:2.3.21")
    implementation("org.jetbrains.kotlin.plugin.compose:org.jetbrains.kotlin.plugin.compose.gradle.plugin:2.3.21")
}

gradlePlugin {
    plugins {
        register("androidApplication") {
            id = "kurastorage.android.application"
            implementationClass = "KuraStorageAndroidApplicationPlugin"
        }
        register("androidLibrary") {
            id = "kurastorage.android.library"
            implementationClass = "KuraStorageAndroidLibraryPlugin"
        }
        register("androidCompose") {
            id = "kurastorage.android.compose"
            implementationClass = "KuraStorageAndroidComposePlugin"
        }
        register("androidTest") {
            id = "kurastorage.android.test"
            implementationClass = "KuraStorageAndroidTestPlugin"
        }
    }
}
