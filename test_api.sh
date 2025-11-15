#!/bin/bash

# Godot MCP HTTP API 测试脚本

API_URL="http://127.0.0.1:7777/"

echo "========================================="
echo "Godot MCP HTTP API 测试"
echo "========================================="
echo ""

# 测试 1: 获取场景树
echo "测试 1: 获取场景树"
echo "请求: POST $API_URL"
echo '{"method":"get_scene_tree","parameters":{}}'
echo ""
curl -X POST "$API_URL" \
  -H "Content-Type: application/json" \
  -d '{"method":"get_scene_tree","parameters":{}}' \
  2>/dev/null | jq .
echo ""
echo "---"
echo ""

# 测试 2: 获取根节点信息
echo "测试 2: 获取根节点信息"
echo "请求: POST $API_URL"
echo '{"method":"get_node_info","parameters":{"nodePath":"/root"}}'
echo ""
curl -X POST "$API_URL" \
  -H "Content-Type: application/json" \
  -d '{"method":"get_node_info","parameters":{"nodePath":"/root"}}' \
  2>/dev/null | jq .
echo ""
echo "---"
echo ""

# 测试 3: 列出资源
echo "测试 3: 列出资源"
echo "请求: POST $API_URL"
echo '{"method":"list_resources","parameters":{"path":"res://"}}'
echo ""
curl -X POST "$API_URL" \
  -H "Content-Type: application/json" \
  -d '{"method":"list_resources","parameters":{"path":"res://"}}' \
  2>/dev/null | jq .
echo ""
echo "---"
echo ""

# 测试 4: 获取性能统计
echo "测试 4: 获取性能统计"
echo "请求: POST $API_URL"
echo '{"method":"get_performance_stats","parameters":{}}'
echo ""
curl -X POST "$API_URL" \
  -H "Content-Type: application/json" \
  -d '{"method":"get_performance_stats","parameters":{}}' \
  2>/dev/null | jq .
echo ""
echo "---"
echo ""

echo "测试完成!"
