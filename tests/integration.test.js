import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {backendStatus,listInstances} from '../runtime/registry.js';
import {invoke} from '../runtime/core.js';
import {installBackend,packRelease} from '../runtime/installer.js';
import {readAddonZip} from '../runtime/archive.js';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const instanceDir=()=>path.join(process.env.GODOT_MCP_HOME,'instances');
function serve(id,projectPath){
 const token='a'.repeat(64),pid=process.pid;
 const server=http.createServer(async(req,res)=>{
  res.setHeader('Content-Type','application/json');
  if(req.headers['x-godot-mcp-token']!==token){res.writeHead(403).end();return}
  if(req.url==='/health')res.end(JSON.stringify({id,pid,protocol:1}));
  else if(req.url==='/call'){
   let data='';for await(const chunk of req)data+=chunk;
   res.end(JSON.stringify({success:true,data:JSON.parse(data)}));
  }else res.writeHead(404).end();
 });
 return new Promise(resolve=>server.listen(0,'127.0.0.1',()=>{
  const port=server.address().port;
  fs.writeFileSync(path.join(instanceDir(),id+'.json'),JSON.stringify({protocol:1,id,pid,port,token,projectPath,projectName:'Demo'}));
  resolve(server);
 }));
}
test('offline, installer, project/process routing, stdio MCP, archive safety',async t=>{
 const temp=fs.mkdtempSync(path.join(os.tmpdir(),'godot-runtime-test-'));
 process.env.GODOT_MCP_HOME=path.join(temp,'state');
 fs.mkdirSync(instanceDir(),{recursive:true});
 const projects=['first','second'].map(name=>{
  const dir=path.join(temp,name);fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir,'project.godot'),'[application]\nconfig/name="Demo"\n[editor_plugins]\nenabled=PackedStringArray("res://addons/other/plugin.cfg")\n');
  fs.writeFileSync(path.join(dir,'Demo.csproj'),'<Project Sdk="Godot.NET.Sdk/4.7.0"/>');
  return dir;
 });
 const servers=[];
 t.after(()=>{servers.forEach(s=>s.close());fs.rmSync(temp,{recursive:true,force:true})});
 assert.deepEqual(await invoke('godot_list_instances'),[]);
 assert.equal(backendStatus(projects[0]).backendInstalled,false);
 await assert.rejects(invoke('get_time',{}),/No matching Godot runtime/);
 const asset=path.join(temp,'release.zip'),pack=packRelease(path.join(root,'addons/godot_mcp'),asset);
 assert.ok(pack.bytes>5000);
 const archive=fs.readFileSync(asset);
 assert.equal(readAddonZip(archive).has('plugin.cfg'),true);
 const malicious=Buffer.from(archive),needle=Buffer.from('GodotMcpPlugin.gd'),
   i=malicious.lastIndexOf(needle);
 assert.ok(i>0);Buffer.from('../malicious/x.cs').copy(malicious,i);
 assert.throws(()=>readAddonZip(malicious),/path traversal/);
 let result=await installBackend(projects[0],{archive:asset});
 assert.equal(result.installedFiles,5);
 result=await installBackend(projects[0],{archive:asset});
 assert.equal(result.installedFiles,5);
 const config=fs.readFileSync(path.join(projects[0],'project.godot'),'utf8');
 assert.match(config,/GodotMcpServer="\*res:\/\//);
 assert.match(config,/res:\/\/addons\/other\/plugin.cfg/);
 assert.equal((config.match(/GodotMcpServer=/g)||[]).length,1);
 const status=await invoke('godot_backend_status',{projectPath:projects[0]});
 assert.equal(status.backendInstalled,true);
 assert.equal(status.autoloadEnabled,true);
 const ids=['1'.repeat(32),'2'.repeat(32),'3'.repeat(32)];
 servers.push(await serve(ids[0],projects[0]));
 servers.push(await serve(ids[1],projects[0]));
 servers.push(await serve(ids[2],projects[1]));
 const found=await listInstances();
 assert.equal(found.length,3);
 assert.ok(found.every(x=>x.token==='a'.repeat(64)));
 const publicList=await invoke('godot_list_instances');
 assert.ok(publicList.every(x=>!Object.hasOwn(x,'token')));
 await assert.rejects(invoke('get_time',{projectPath:projects[0]}),/Ambiguous/);
 const one=await invoke('get_time',{projectPath:projects[0],instanceId:ids[1]});
 assert.equal(one.instance.id,ids[1]);
 const two=await invoke('get_time',{projectPath:projects[1]});
 assert.equal(two.instance.id,ids[2]);
 const child=spawn(process.execPath,[path.join(root,'bin/godot-mcp.js'),'mcp'],{env:{...process.env,GODOT_MCP_HOME:path.join(temp,'empty')}});
 t.after(()=>child.kill());
 const output=[];
 child.stdout.setEncoding('utf8');
 let remaining='';
 const done=new Promise((resolve,reject)=>{
  child.stdout.on('data',str=>{
   remaining+=str;const lines=remaining.split('\n');remaining=lines.pop();
   for(const line of lines)if(line.trim())output.push(JSON.parse(line));
   if(output.length>=2)resolve();
  });
  child.on('error',reject);
  setTimeout(()=>reject(new Error('MCP stdio timeout')),5000).unref();
 });
 child.stdin.write(JSON.stringify({jsonrpc:'2.0',id:1,method:'initialize',params:{protocolVersion:'2025-03-26'}})+'\n');
 child.stdin.write(JSON.stringify({jsonrpc:'2.0',id:2,method:'tools/list'})+'\n');
 await done;
 assert.equal(output.find(x=>x.id===1).result.serverInfo.name,'godot-runtime-mcp');
 assert.ok(output.find(x=>x.id===2).result.tools.some(x=>x.name==='godot_install_backend'));
});
