#!/usr/bin/env node
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {invoke,allTools} from '../runtime/core.js';
import {listInstances,publicInstance,backendStatus} from '../runtime/registry.js';
import {installBackend,packRelease} from '../runtime/installer.js';
import {runMcp} from '../runtime/mcp.js';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
function parse(argv){
 const positional=[],opts={};
 for(let i=0;i<argv.length;i++){
  let token=argv[i];
  if(!token.startsWith('--')){positional.push(token);continue}
  token=token.slice(2);const eq=token.indexOf('=');
  const key=eq<0?token:token.slice(0,eq),value=eq<0?argv[++i]:token.slice(eq+1);
  if(!value||value.startsWith('--'))throw Error('Missing value for --'+key);
  opts[key]=value;
 }
 return {positional,opts};
}
async function main(){
 const cmd=process.argv[2]||'help';
 if(cmd==='mcp'){runMcp();return}
 if(cmd==='help'||cmd==='--help'){console.log(
'Godot Runtime MCP v3 (Node.js >=20, Godot .NET)\n'+
'  godot-mcp instances [--project PATH]\n'+
'  godot-mcp doctor --project PATH\n'+
'  godot-mcp tools\n'+
'  godot-mcp call TOOL --project PATH [--instance ID] [--args JSON]\n'+
'  godot-mcp install PATH [--version TAG] [--archive ZIP]\n'+
'  godot-mcp pack [--output ZIP]\n'+
'  godot-mcp mcp  (MCP stdio server; works when no game is running)\n');return}
 const {positional,opts}=parse(process.argv.slice(3));
 const project=opts.project||opts.projectPath;
 let out;
 switch(cmd){
  case 'instances':out=(await listInstances()).filter(x=>!project||x.projectPath===path.resolve(project)).map(publicInstance);break;
  case 'doctor':{
   if(!project)throw Error('doctor requires --project PATH');
   out=await invoke('godot_backend_status',{projectPath:project});break;
  }
  case 'tools':out=allTools;break;
  case 'call':{
   if(!positional[0])throw Error('call requires TOOL');
   let args={};
   if(opts.args){try{args=JSON.parse(opts.args)}catch{throw Error('--args must be valid JSON')}}
   if(!args||Array.isArray(args)||typeof args!=='object')throw Error('--args must be a JSON object');
   out=await invoke(positional[0],{...args,...(project?{projectPath:project}:{}),
      ...(opts.instance?{instanceId:opts.instance}:{})});break;
  }
  case 'install':{
   const dir=positional[0]||project;
   if(!dir)throw Error('install requires a Godot project path');
   out=await installBackend(dir,{version:opts.version||'latest',archive:opts.archive});break;
  }
  case 'pack':out=packRelease(path.join(root,'addons','godot_mcp'),opts.output||path.join(root,'dist','godot-mcp-addon.zip'));break;
  default:throw Error('Unknown command '+cmd);
 }
 console.log(JSON.stringify(out,null,2));
}
main().catch(e=>{console.error('godot-mcp:',e.message);process.exitCode=1});
