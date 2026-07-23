plugins {
    id("kurastorage.android.library")
    id("kurastorage.android.test")
}

android.namespace = "com.kurastorage.core.security"

dependencies {
    implementation(project(":core-model"))
}
