import readline from 'node:readline';
import {allTools,invoke} from './core.js';
const version='2025-03-26';
const enc=value=>JSON.stringify(value);
function write(message){process.stdout.write(enc(message)+'\n')}
async function handle(request){
 if(!request||request.jsonrpc!=='2.0'||typeof request.method!=='string'){
  return {jsonrpc:'2.0',id:request?.id??null,error:{code:-32600,message:'Invalid JSON-RPC request'}};
 }
 if(!Object.hasOwn(request,'id'))return null;
 const id=request.id;
 const answer=result=>({jsonrpc:'2.0',id,result});
 const failure=(code,message)=>({jsonrpc:'2.0',id,error:{code,message}});
 try{
  switch(request.method){
   case 'initialize':return answer({
    protocolVersion:request.params?.protocolVersion||version,
    capabilities:{tools:{listChanged:false}},serverInfo:{name:'godot-runtime-mcp',version:'3.0.0'},
    instructions:'Management and installation tools work offline. Select projectPath and instanceId for multiple running processes.'});
   case 'ping':return answer({});
   case 'tools/list':return answer({tools:allTools});
   case 'resources/list':return answer({resources:[]});
   case 'prompts/list':return answer({prompts:[]});
   case 'tools/call':{
    const name=request.params?.name,args=request.params?.arguments||{};
    if(typeof name!=='string'||!allTools.some(t=>t.name===name))return failure(-32602,'Unknown/missing tool');
    try{
     const data=await invoke(name,args);
     return answer({content:[{type:'text',text:enc(data)}],structuredContent:{result:data}});
    }catch(e){return answer({isError:true,content:[{type:'text',text:e.message}]})}
   }
   default:return failure(-32601,'Method not found: '+request.method);
  }
 }catch(e){return failure(-32603,e.message)}
}
export function runMcp(){
 // Stdio MCP uses one JSON-RPC object per line. Never print logs to stdout.
 const lines=readline.createInterface({input:process.stdin,crlfDelay:Infinity});
 lines.on('line',line=>{
  if(!line.trim())return;
  let request;
  try{request=JSON.parse(line)}catch{write({jsonrpc:'2.0',id:null,error:{code:-32700,message:'Parse error'}});return}
  Promise.resolve(handle(request)).then(response=>{if(response)write(response)})
   .catch(e=>console.error('MCP request error:',e));
 });
}
