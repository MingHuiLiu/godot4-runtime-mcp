import {runtimeNames,runtimeTools,managementTools,allTools} from './catalog.js';
import {listInstances,chooseInstance,publicInstance,backendStatus} from './registry.js';
import {installBackend} from './installer.js';
export {allTools};
export class ToolError extends Error{}
export async function invoke(name,args={}){
 if(!args||typeof args!=='object'||Array.isArray(args))throw new ToolError('Tool arguments must be a JSON object');
 if(name==='godot_list_instances')return (await listInstances()).map(publicInstance);
 if(name==='godot_list_runtime_tools')return runtimeTools;
 if(name==='godot_backend_status'){
  if(!args.projectPath)throw new ToolError('projectPath is required');
  const status=backendStatus(args.projectPath);
  const instances=await listInstances();
  return {...status,instances:instances.filter(x=>x.projectPath===status.projectPath).map(publicInstance)};
 }
 if(name==='godot_install_backend'){
  if(!args.projectPath)throw new ToolError('projectPath is required');
  return installBackend(args.projectPath,{version:args.version||'latest'});
 }
 if(!runtimeNames.has(name))throw new ToolError('Unknown tool '+name);
 const schema=runtimeTools.find(x=>x.name===name).inputSchema;
 for(const required of schema.required||[])if(!(required in args))throw new ToolError('Missing required argument '+required);
 const instance=chooseInstance(await listInstances(),args);
 const {projectPath,instanceId,...parameters}=args;
 let response;
 try{
  response=await fetch('http://127.0.0.1:'+instance.port+'/call',{
   method:'POST',headers:{'Content-Type':'application/json','X-Godot-MCP-Token':instance.token},
   body:JSON.stringify({name,arguments:parameters}),signal:AbortSignal.timeout(30000)});
 }catch(e){throw new ToolError('Godot runtime disconnected or timed out: '+e.message)}
 if(!response.ok)throw new ToolError('Godot runtime HTTP '+response.status+': '+(await response.text()).slice(0,500));
 const result=await response.json();
 if(!result.success)throw new ToolError(result.error||'Godot runtime operation failed');
 return {instance:publicInstance(instance),data:result.data};
}
