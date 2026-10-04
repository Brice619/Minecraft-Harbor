import java.net.InetSocketAddress;
import java.nio.file.*;
import java.lang.reflect.Proxy;
import java.util.Optional;
import pl.skidam.automodpack_core.config.Jsons;
import pl.skidam.automodpack_core.utils.HarborPackRequirement;
import pl.skidam.automodpack_core.utils.AutoModpackProtocol;
import pl.skidam.automodpack_loader_core.client.ModpackUpdater;
import pl.skidam.automodpack_loader_core.screen.ScreenManager;
import pl.skidam.automodpack_loader_core.screen.ScreenService;
import static pl.skidam.automodpack_core.GlobalVariables.*;

public class PackGateTests {
    private static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
    private static void metadata(Path root,long project,long file,String label)throws Exception{
        Files.writeString(root.resolve("minecraftinstance.json"),"{\"name\":\"Renamed profile\",\"installedModpack\":{\"addonID\":"+project+",\"installedFile\":{\"id\":"+file+",\"fileName\":\""+label+"\"}}}");
    }
    public static void main(String[] args)throws Exception {
        Path root=Files.createTempDirectory(Path.of(args[0]),"pack-gate-");
        HarborPackRequirement required=new HarborPackRequirement();required.name="ATM10";required.version="8.1";required.projectId=925200;required.clientFileId=8764211;
        metadata(root,925200,8764211,"ATM10-8.1.zip");
        Files.writeString(root.resolve("manifest.json"),"{\"version\":\"7.3\"}");
        check(required.mismatch(root)==null,"Exact installed CurseForge file must override stale exported manifest and renamed profile");
        metadata(root,925200,123,"ATM10-7.3.zip");
        String error=required.mismatch(root);check(error.contains("8.1")&&error.contains("7.3"),"Mismatch must show both releases");
        metadata(root,999,8764211,"ATM10-8.1.zip");check(required.mismatch(root)!=null,"Wrong project cannot pass by matching a label or file ID");
        Files.writeString(root.resolve("minecraftinstance.json"),"{");check(required.mismatch(root)!=null,"Malformed metadata must fail closed");
        Files.delete(root.resolve("minecraftinstance.json"));check(required.mismatch(root)!=null,"Missing metadata must fail closed");
        metadata(root,925200,123,"ATM10-7.3.zip");
        Path mods=root.resolve("mods");Files.createDirectories(mods);Files.writeString(mods.resolve("existing.jar"),"unchanged");MODS_DIR=mods;preload=false;
        final String[] shown={null};final boolean[] continued={false};
        ScreenManager.INSTANCE=(ScreenService)Proxy.newProxyInstance(ScreenService.class.getClassLoader(),new Class[]{ScreenService.class},(p,m,a)->{
            if(m.getName().equals("error"))shown[0]=((String[])a[0])[0];
            if(m.getName().equals("danger"))continued[0]=true;
            return m.getReturnType()==Optional.class?Optional.empty():null;
        });
        var content=new Jsons.ModpackContentFields();content.harbor=required;
        var address=new InetSocketAddress("127.0.0.1",25565);
        Path download=root.resolve("automodpack/modpacks/ATM10");
        new ModpackUpdater(content,new Jsons.ModpackAddresses(address,address,true),null,download).processModpackUpdate(null);
        check(shown[0]!=null&&shown[0].contains("8.1"),"Interactive mismatch screen must appear");
        check(!Files.exists(download),"Mismatch must stop before creating a download folder");
        check(Files.readString(mods.resolve("existing.jar")).equals("unchanged"),"Mismatch must not change existing mods");
        preload=true;shown[0]=null;
        new ModpackUpdater(content,new Jsons.ModpackAddresses(address,address,true),null,download).processModpackUpdate(null);
        check(!Files.exists(download),"Startup preload must also stop before downloads");
        Files.createDirectories(download);
        Files.writeString(download.resolve(hostModpackContentFile.getFileName()),pl.skidam.automodpack_core.config.ConfigTools.GSON.toJson(content));
        shown[0]=null;preload=false;
        new ModpackUpdater(null,new Jsons.ModpackAddresses(address,address,true),null,download).processModpackUpdate(null);
        check(shown[0]!=null,"Cached pack activation must also check the version");
        check(Files.readString(mods.resolve("existing.jar")).equals("unchanged"),"Cached mismatch must not apply pack files");
        metadata(root,925200,8764211,"ATM10-8.1.zip");shown[0]=null;
        Path matchingDownload=root.resolve("automodpack/modpacks/Matching");
        new ModpackUpdater(content,new Jsons.ModpackAddresses(address,address,true),null,matchingDownload).processModpackUpdate(null);
        check(continued[0]&&Files.isDirectory(matchingDownload)&&shown[0]==null,"Matching release must continue into normal AutoModpack setup");
        required.clientFileId=0;check(required.mismatch(root).contains("host must select"),"Missing server release ID must explain how the host fixes it");
        check(!AutoModpackProtocol.acceptsClient("4.0.6-harbor14","4.0.6"),"Unmodified helper must not bypass version checking");
        check(AutoModpackProtocol.acceptsClient("4.0.6-harbor14","4.0.6-harbor14"),"Matching Harbor helpers must connect");
        System.out.println("Passed: exact release identity, renamed profile, stale manifest, wrong version/project, unreadable metadata, in-game error, no download/file writes, startup preflight, helper enforcement");
    }
}
