import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import pl.skidam.automodpack_core.config.*;

/** Host-side configuration only. Never installed in a Minecraft client or a mods folder. */
public final class ConfigureAutoModpack {
    public static void main(String[] args) throws Exception {
        Path directory = Path.of(args[0]).resolve("automodpack");
        Path configFile = directory.resolve("server.conf");
        var config = ReconfConfigs.readOrCreate(configFile, ServerConfigJsons.ServerConfigFieldsV3.class,
                ServerConfigJsons.ServerConfigFieldsV3::new);
        if (args[1].equals("--inspect")) {
            Files.writeString(Path.of(args[2]), ConfigTools.GSON.toJson(config), StandardCharsets.UTF_8);
            return;
        }
        String name = args[1], version = args[2];
        String reminder = version.isBlank() ? "Before continuing: use " + name + " in CurseForge"
                : "Before continuing: use " + name + " " + version + " in CurseForge";
        config.modpackHost = true;
        config.generateModpackOnStart = true;
        config.requireModpack = true;
        config.selfUpdater = false;
        if (config.modpack.name.isBlank()) config.modpack.name = name;
        // Keep permanent group identities, including custom categories, unchanged.
        var main = config.modpack.categories.values().stream().filter(g -> g.containsKey("main"))
                .map(g -> g.get("main")).findFirst().orElseThrow(() ->
                        new IllegalStateException("AutoModpack's main group is missing. Restore it before starting."));
        main.displayName = reminder;
        main.description = "Pack-version reminder only; Harbor does not block syncing when versions differ.";
        main.fromServer = new LinkedHashSet<>(main.fromServer);
        main.exclude = new LinkedHashSet<>(main.exclude);
        // V4 negated rules become explicit V5 exclusions, including operator additions.
        for (String rule : new ArrayList<>(main.fromServer)) {
            if (rule.startsWith("!")) { main.fromServer.remove(rule); main.exclude.add(rule.substring(1).replaceFirst("^/+", "")); }
        }
        main.fromServer.addAll(List.of("mods/*.jar", "kubejs/**", "config/**", "emotes/*"));
        main.exclude.addAll(List.of("kubejs/server_scripts/**", "config/fancymenu/user_variables.db"));
        main.editable = new LinkedHashSet<>(main.editable);
        main.editable.removeIf(s -> s.equals("config/**") || s.equals("/config/**"));
        ReconfConfigs.save(configFile, config, ServerConfigJsons.ServerConfigFieldsV3.class,
                ServerConfigJsons.ServerConfigFieldsV3::new);
        Path notesFile = directory.resolve("host-modpack/patch-notes.md");
        Files.createDirectories(notesFile.getParent());
        String start = "<!-- Harbor pack reminder -->", end = "<!-- /Harbor pack reminder -->";
        String notes = Files.exists(notesFile) ? Files.readString(notesFile) : "";
        int a = notes.indexOf(start), b = a < 0 ? -1 : notes.indexOf(end, a);
        if (a >= 0 && b >= a) notes = notes.substring(0, a) + notes.substring(b + end.length());
        Files.writeString(notesFile, start + "\n" + reminder + ".\n" +
                "Check your CurseForge profile before confirming the download.\n" + end + "\n" + notes.stripLeading(), StandardCharsets.UTF_8);
    }
}
