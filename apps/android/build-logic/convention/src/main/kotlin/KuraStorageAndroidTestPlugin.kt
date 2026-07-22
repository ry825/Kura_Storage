import org.gradle.api.Plugin
import org.gradle.api.Project

class KuraStorageAndroidTestPlugin : Plugin<Project> {
    override fun apply(target: Project) {
        target.dependencies.add("testImplementation", "junit:junit:4.13.2")
    }
}
