import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
export const stateHome=()=>path.resolve(process.env.GODOT_MCP_HOME||path.join(os.homedir(),'.godot-runtime-mcp'));
export const registryDir=()=>path.join(stateHome(),'instances');
export function canonical(p){
 if(!p||typeof p!=='string')throw Error('Specify a Godot project path');
 const full=path.resolve(p);try{return fs.realpathSync(full)}catch{return full}
}
export const isGodotProject=p=>fs.existsSync(path.join(canonical(p),'project.godot'));
export function backendStatus(p){
 const projectPath=canonical(p), dir=path.join(projectPath,'addons','godot_mcp');
 const settingsFile=path.join(projectPath,'project.godot');
 const settings=fs.existsSync(settingsFile)?fs.readFileSync(settingsFile,'utf8'):'';
 return {projectPath,projectExists:isGodotProject(projectPath),
 backendInstalled:fs.existsSync(path.join(dir,'plugin.cfg'))&&fs.existsSync(path.join(dir,'GodotMcpServer.cs')),
 autoloadScenePresent:fs.existsSync(path.join(dir,'mcp_server.tscn')),
 autoloadEnabled:/^\s*GodotMcpServer\s*=\s*"\*res:\/\/addons\/godot_mcp\/mcp_server\.tscn"/m.test(settings),
 pluginConfigured:settings.includes('"res://addons/godot_mcp/plugin.cfg"')};
}
export async function listInstances({timeoutMs=500}={}){
 let files=[];try{files=fs.readdirSync(registryDir()).filter(x=>/^[a-f0-9]{32}\.json$/i.test(x))}
 catch(e){if(e.code!=='ENOENT')throw e}
 const entries=files.flatMap(file=>{try{
  const name=path.join(registryDir(),file),stat=fs.lstatSync(name);
  if(!stat.isFile()||stat.size>8192)return [];
  const x=JSON.parse(fs.readFileSync(name,'utf8'));
  if(x.protocol!==1||x.id+'.json'!==file||!/^[a-f0-9]{32}$/i.test(x.id)||
    !Number.isInteger(x.port)||x.port<1024||x.port>65535||
    !/^[a-f0-9]{64}$/i.test(x.token)||!Number.isSafeInteger(x.pid)||x.pid<=0||
    typeof x.projectPath!=='string')return [];
  return [x];
 }catch{return []}});
 const results=await Promise.all(entries.map(async x=>{try{
  const res=await fetch('http://127.0.0.1:'+x.port+'/health',{headers:{'X-Godot-MCP-Token':x.token},signal:AbortSignal.timeout(timeoutMs)});
  if(!res.ok)return null;
  const check=await res.json();
  if(check.id!==x.id||check.pid!==x.pid||check.protocol!==1)return null;
  return {id:x.id,pid:x.pid,port:x.port,projectPath:canonical(x.projectPath),
    projectName:x.projectName,startedAt:x.startedAt,token:x.token};
 }catch{return null}}));
 return results.filter(Boolean).sort((a,b)=>a.projectPath.localeCompare(b.projectPath)||a.pid-b.pid);
}
export function publicInstance(x){const {token,...clean}=x;return clean}
export function chooseInstance(instances,{projectPath,instanceId}={}){
 let list=instances;
 if(projectPath)list=list.filter(i=>i.projectPath===canonical(projectPath));
 if(instanceId)list=list.filter(i=>i.id===instanceId);
 if(list.length===1)return list[0];
 if(!list.length)throw Error('No matching Godot runtime. Install the addon, enable its autoload and launch the game; CLI/MCP management tools work offline.');
 throw Error('Ambiguous: multiple Godot processes. Select projectPath and instanceId: '+list.map(i=>i.id+' '+i.projectPath+' pid='+i.pid).join('; '));
}
