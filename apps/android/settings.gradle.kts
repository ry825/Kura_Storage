pluginManagement {
    includeBuild("build-logic")
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "KuraStorageAndroid"

include(
    ":app",
    ":core-model",
    ":core-network",
    ":core-data",
    ":core-security",
    ":core-ui",
    ":feature-connection",
    ":feature-auth",
    ":feature-files",
)
