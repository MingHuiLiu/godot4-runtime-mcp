import {readFileSync} from 'node:fs';
export const runtimeTools=Object.freeze(JSON.parse(readFileSync(new URL('./tools.json',import.meta.url),'utf8')));
export const runtimeNames=new Set(runtimeTools.map(t=>t.name));
const selector={projectPath:{type:'string',description:'Absolute Godot project directory'},instanceId:{type:'string',description:'Exact runtime instance ID'}};
export const managementTools=[
{name:'godot_list_instances',description:'List running Godot project processes, even when none is connected.',inputSchema:{type:'object',properties:{}}},
{name:'godot_backend_status',description:'Check whether a project and backend are installed, independently of running Godot.',inputSchema:{type:'object',properties:{projectPath:{type:'string'}},required:['projectPath']}},
{name:'godot_install_backend',description:'Install the Godot C# backend into a project from a GitHub Release ZIP asset.',inputSchema:{type:'object',properties:{projectPath:{type:'string'},version:{type:'string',description:'Release tag, default latest'}},required:['projectPath']}},
{name:'godot_list_runtime_tools',description:'Show supported runtime operations without requiring Godot.',inputSchema:{type:'object',properties:{}}}
];
export const allTools=Object.freeze([...managementTools,...runtimeTools.map(t=>({...t,inputSchema:{...t.inputSchema,properties:{...t.inputSchema.properties,...selector}}}))]);
