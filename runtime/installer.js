import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {canonical,isGodotProject} from './registry.js';
import {readAddonZip,createAddonZip} from './archive.js';
const api='https://api.github.com/repos/MingHuiLiu/godot4-runtime-mcp';
async function fetchRelease(version){
 const endpoint=version==='latest'?api+'/releases/latest':api+'/releases/tags/'+encodeURIComponent(version);
 const r=await fetch(endpoint,{headers:{Accept:'application/vnd.github+json','User-Agent':'godot-runtime-mcp-cli'},signal:AbortSignal.timeout(15000)});
 if(!r.ok)throw Error('Release unavailable ('+r.status+'). Publish godot-mcp-addon.zip manually using godot-mcp pack. No GitHub Actions required.');
 const release=await r.json();
 const asset=release.assets?.find(x=>x.name==='godot-mcp-addon.zip')||
  release.assets?.find(x=>/^godot[-_]mcp[-_]addon.*\.zip$/i.test(x.name));
 if(!asset)throw Error('Release '+release.tag_name+' has no godot-mcp-addon.zip asset');
 if(asset.size>40*1024*1024)throw Error('Release asset too large');
 const data=await fetch(asset.browser_download_url,{headers:{'User-Agent':'godot-runtime-mcp-cli'},signal:AbortSignal.timeout(30000)});
 if(!data.ok)throw Error('Release download failed ('+data.status+')');
 const bytes=Buffer.from(await data.arrayBuffer());if(bytes.length>40*1024*1024)throw Error('Release asset too large');
 if(asset.digest?.startsWith('sha256:')&&crypto.createHash('sha256').update(bytes).digest('hex')!==asset.digest.slice(7))
  throw Error('Release SHA256 mismatch');
 return {bytes,source:release.tag_name};
}
function configure(content,assemblyName){
 let text=content.replace(/\r\n/g,'\n');
 const set=(section,key,value)=>{
  const heading='['+section+']';let start=text.indexOf(heading);
  if(start===-1){text+='\n'+heading+'\n'+key+'='+value+'\n';return}
  const from=start+heading.length,tail=text.slice(from),next=tail.search(/\n\[/);
  const to=next<0?text.length:from+next+1;
  const chunk=text.slice(from,to),re=new RegExp('^'+key+'\\s*=.*$','m');
  text=text.slice(0,from)+(re.test(chunk)?chunk.replace(re,key+'='+value):chunk+(chunk.endsWith('\n')?'':'\n')+key+'='+value+'\n')+text.slice(to);
 };
 const plugin='res://addons/godot_mcp/plugin.cfg';
 const line=text.match(/^\s*enabled\s*=\s*PackedStringArray\(([^)]*)\)/m);
 const plugins=line?[...line[1].matchAll(/"([^"]+)"/g)].map(x=>x[1]):[];
 if(!plugins.includes(plugin))plugins.push(plugin);
 if(!/^\s*project\/assembly_name\s*=/m.test(text))set('dotnet','project/assembly_name',JSON.stringify(assemblyName));
 set('autoload','GodotMcpServer','"*res://addons/godot_mcp/mcp_server.tscn"');
 set('editor_plugins','enabled','PackedStringArray('+plugins.map(x=>JSON.stringify(x)).join(', ')+')');
 return text;
}
export async function installBackend(project,{version='latest',archive}={}){
 const projectPath=canonical(project);
 if(!isGodotProject(projectPath))throw Error('project.godot missing in '+projectPath);
 const csproj=fs.readdirSync(projectPath).find(x=>x.endsWith('.csproj'));
 if(!csproj)throw Error('Godot .NET project required: .csproj not found');
 const content=fs.readFileSync(path.join(projectPath,csproj),'utf8');
 const assemblyName=content.match(/<AssemblyName>([^<]+)<\/AssemblyName>/)?.[1]||path.basename(csproj,'.csproj');
 const source=archive?{bytes:fs.readFileSync(archive),source:path.resolve(archive)}:await fetchRelease(version);
 const files=readAddonZip(source.bytes),addons=path.join(projectPath,'addons');
 fs.mkdirSync(addons,{recursive:true});
 const target=path.join(addons,'godot_mcp'),stage=fs.mkdtempSync(path.join(addons,'.godot-mcp-stage-')),
 backup=path.join(addons,'.godot-mcp-backup-'+crypto.randomUUID()),projFile=path.join(projectPath,'project.godot');
 const original=fs.readFileSync(projFile,'utf8');let old=false,newInstalled=false;
 try{
  for(const [relative,data] of files){
   const filename=path.join(stage,...relative.split('/'));
   fs.mkdirSync(path.dirname(filename),{recursive:true});fs.writeFileSync(filename,data,{flag:'wx'});
  }
  if(fs.existsSync(target)){
   if(fs.lstatSync(target).isSymbolicLink())throw Error('Refusing to overwrite addon symlink');
   fs.renameSync(target,backup);old=true;
  }
  fs.renameSync(stage,target);newInstalled=true;
  fs.writeFileSync(projFile,configure(original,assemblyName),'utf8');
  if(old)fs.rmSync(backup,{recursive:true,force:true});
  return {projectPath,source:source.source,installedFiles:files.size,addon:target,
    note:'Restart Godot editor/game and compile the Godot .NET project'};
 }catch(e){
  try{fs.writeFileSync(projFile,original,'utf8')}catch{}
  if(newInstalled)fs.rmSync(target,{recursive:true,force:true});
  if(old)fs.renameSync(backup,target);
  throw e;
 }finally{if(fs.existsSync(stage))fs.rmSync(stage,{recursive:true,force:true})}
}
export function packRelease(addonDir,output){
 const zip=createAddonZip(addonDir);fs.mkdirSync(path.dirname(path.resolve(output)),{recursive:true});
 fs.writeFileSync(output,zip);
 return {asset:path.resolve(output),bytes:zip.length,sha256:crypto.createHash('sha256').update(zip).digest('hex')};
}
