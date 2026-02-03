CREATE TABLE IF NOT EXISTS `jsh_cashier_session_product_item` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT COMMENT '主键',
  `session_id` bigint(20) DEFAULT NULL COMMENT '收银会话id',
  `material_id` bigint(20) DEFAULT NULL COMMENT '商品id(对应jsh_material.id)',
  `material_name_snap` varchar(200) DEFAULT NULL COMMENT '商品名称快照',
  `unit_price` decimal(24,6) DEFAULT '0.000000' COMMENT '单价',
  `qty` decimal(24,6) DEFAULT '1.000000' COMMENT '数量',
  `amount` decimal(24,6) DEFAULT '0.000000' COMMENT '金额',
  `create_time` datetime DEFAULT NULL COMMENT '创建时间',
  `tenant_id` bigint(20) DEFAULT NULL COMMENT '租户id',
  `delete_flag` varchar(1) DEFAULT '0' COMMENT '删除标记，0未删除，1删除',
  PRIMARY KEY (`id`),
  KEY `idx_session` (`session_id`),
  KEY `idx_tenant` (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='收银会话商品明细(购物车)';
