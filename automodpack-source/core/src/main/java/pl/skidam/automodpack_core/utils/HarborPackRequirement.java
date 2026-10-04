package pl.skidam.automodpack_core.utils;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/** Harbor 1.4: verifies the installed CurseForge release before changing pack files. */
public final class HarborPackRequirement {
    public String name = "";
    public String version = "";
    public long projectId;
    public long clientFileId;

    public static HarborPackRequirement readServer(Path file) {
        if (!Files.isRegularFile(file)) return null;
        try (var reader = Files.newBufferedReader(file)) {
            var requirement = pl.skidam.automodpack_core.config.ConfigTools.GSON.fromJson(reader, HarborPackRequirement.class);
            if (requirement == null || requirement.projectId <= 0 || requirement.version.isBlank())
                throw new IOException("Incomplete Harbor pack requirement");
            return requirement;
        } catch (Exception e) {
            throw new IllegalStateException("Cannot read Harbor's required pack version", e);
        }
    }

    public String mismatch(Path instance) {
        String required = "This server requires " + name + " " + version + ".";
        if (clientFileId <= 0)
            return required + " The host must select its matching CurseForge client profile in Harbor. Syncing is paused until the release can be verified.";
        Path metadata = instance.resolve("minecraftinstance.json");
        try {
            if (!Files.isRegularFile(metadata) || Files.size(metadata) > 32L * 1024 * 1024)
                return required + " Could not verify this CurseForge profile. Open the matching profile in CurseForge, then reconnect. No pack files were changed.";
            JsonObject root;
            try (var reader = Files.newBufferedReader(metadata)) {
                root = JsonParser.parseReader(reader).getAsJsonObject();
            }
            JsonObject pack = object(root, "installedModpack");
            JsonObject installedFile = object(pack, "installedFile");
            long installedProject = number(pack, "addonID");
            long installedFileId = number(installedFile, "id");
            // File IDs identify the release regardless of a renamed profile or stale export manifest.
            if (installedProject == projectId && clientFileId > 0 && installedFileId == clientFileId) return null;
            String installedVersion = text(installedFile, "displayName");
            if (installedVersion.isBlank()) installedVersion = text(installedFile, "fileName");
            if (installedVersion.isBlank()) installedVersion = "an unknown release";
            return required + " Your profile has " + installedVersion + ". Update this profile in CurseForge, then reconnect. No mods, configs or scripts were changed.";
        } catch (Exception e) {
            return required + " Could not read this profile's CurseForge version. Open the matching profile in CurseForge, then reconnect. No pack files were changed.";
        }
    }

    private static JsonObject object(JsonObject object, String key) {
        return object != null && object.has(key) && object.get(key).isJsonObject() ? object.getAsJsonObject(key) : new JsonObject();
    }
    private static String text(JsonObject object, String key) {
        return object.has(key) && object.get(key).isJsonPrimitive() ? object.get(key).getAsString() : "";
    }
    private static long number(JsonObject object, String key) {
        try { return object.has(key) ? object.get(key).getAsLong() : 0; }
        catch (RuntimeException e) { return 0; }
    }
}
