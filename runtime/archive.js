import fs from 'node:fs';
import path from 'node:path';
import {deflateRawSync,inflateRawSync} from 'node:zlib';
const table=Array.from({length:256},(_,i)=>{let c=i;for(let k=0;k<8;k++)c=(c&1)?0xedb88320^(c>>>1):c>>>1;return c>>>0});
const crc32=b=>{let c=0xffffffff;for(const x of b)c=table[(c^x)&255]^(c>>>8);return (c^0xffffffff)>>>0};
const u16=(b,i,n)=>b.writeUInt16LE(n,i),u32=(b,i,n)=>b.writeUInt32LE(n>>>0,i);
export function createAddonZip(dir){
 const files=[];function walk(p){for(const ent of fs.readdirSync(p,{withFileTypes:true})){
  if(ent.isSymbolicLink())throw Error('Symlinks forbidden in ZIP');
  const next=path.join(p,ent.name);
  if(ent.isDirectory())walk(next);else if(ent.isFile())files.push(next);
 }}walk(dir);
 const locals=[],entries=[];let offset=0;
 for(const file of files){
  const name=Buffer.from('addons/godot_mcp/'+path.relative(dir,file).split(path.sep).join('/'));
  const data=fs.readFileSync(file),packed=deflateRawSync(data),crc=crc32(data);
  const local=Buffer.alloc(30);u32(local,0,0x04034b50);u16(local,4,20);u16(local,6,0x800);u16(local,8,8);
  u32(local,14,crc);u32(local,18,packed.length);u32(local,22,data.length);u16(local,26,name.length);
  locals.push(local,name,packed);
  const central=Buffer.alloc(46);u32(central,0,0x02014b50);u16(central,4,20);u16(central,6,20);u16(central,8,0x800);u16(central,10,8);
  u32(central,16,crc);u32(central,20,packed.length);u32(central,24,data.length);u16(central,28,name.length);u32(central,42,offset);
  entries.push(central,name);offset+=local.length+name.length+packed.length;
 }
 if(files.length>65535)throw Error('Too many ZIP entries');
 const cd=Buffer.concat(entries),end=Buffer.alloc(22);u32(end,0,0x06054b50);
 u16(end,8,files.length);u16(end,10,files.length);u32(end,12,cd.length);u32(end,16,offset);
 return Buffer.concat([...locals,cd,end]);
}
export function readAddonZip(b){
 if(b.length>40*1024*1024)throw Error('Release ZIP exceeds size limit');
 let end=-1;for(let i=b.length-22;i>=Math.max(0,b.length-65557);i--)if(b.readUInt32LE(i)===0x06054b50){end=i;break}
 if(end<0)throw Error('ZIP central directory not found');
 const count=b.readUInt16LE(end+10),dirBytes=b.readUInt32LE(end+12);let at=b.readUInt32LE(end+16),total=0;
 if(count>1000||at+dirBytes>end)throw Error('Invalid ZIP directory');
 const result=new Map();
 for(let i=0;i<count;i++){
  if(at+46>b.length||b.readUInt32LE(at)!==0x02014b50)throw Error('Bad ZIP entry');
  const flags=b.readUInt16LE(at+8),method=b.readUInt16LE(at+10),crc=b.readUInt32LE(at+16),packed=b.readUInt32LE(at+20),
   size=b.readUInt32LE(at+24),len=b.readUInt16LE(at+28),extra=b.readUInt16LE(at+30),comment=b.readUInt16LE(at+32),
   attr=b.readUInt32LE(at+38),offset=b.readUInt32LE(at+42);
  if(at+46+len+extra+comment>b.length)throw Error('Truncated ZIP directory');
  const filename=b.subarray(at+46,at+46+len).toString('utf8');at+=46+len+extra+comment;
  if(flags&1||![0,8].includes(method))throw Error('ZIP encryption/compression unsupported');
  const parts=filename.replaceAll('\\','/').split('/');
  if(parts.includes('..')||filename.startsWith('/')||/^[A-Za-z]:/.test(filename)||filename.includes('\0'))throw Error('ZIP path traversal');
  const pos=parts.indexOf('godot_mcp');
  if(pos<0||pos>1||(pos===1&&parts[0]!=='addons'))continue;
  const rel=parts.slice(pos+1).join('/');
  if(!rel||filename.endsWith('/'))continue;
  if(!/^[\w.\-/]+$/.test(rel)||rel.startsWith('.'))throw Error('Invalid addon filename');
  if((attr>>>16&0xf000)===0xa000)throw Error('ZIP symlinks forbidden');
  total+=size;
  if(result.has(rel)||size>8*1024*1024||total>32*1024*1024)throw Error('Oversized/duplicate addon file');
  if(offset+30>b.length||b.readUInt32LE(offset)!==0x04034b50)throw Error('Bad ZIP local header');
  const from=offset+30+b.readUInt16LE(offset+26)+b.readUInt16LE(offset+28);
  if(from+packed>b.length)throw Error('Truncated ZIP file');
  const bytes=method===8?inflateRawSync(b.subarray(from,from+packed),{maxOutputLength:size+1}):b.subarray(from,from+packed);
  if(bytes.length!==size||crc32(bytes)!==crc)throw Error('ZIP checksum error: '+rel);
  result.set(rel,bytes);
 }
 for(const required of ['plugin.cfg','GodotMcpPlugin.gd','GodotMcpServer.cs','mcp_server.tscn'])if(!result.has(required))throw Error('Addon missing '+required);
 return result;
}
