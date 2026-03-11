package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.ServiceOrder;
import com.jsh.erp.datasource.entities.ServiceOrderItem;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.ServiceOrderService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping(value = "/cashier/serviceOrder")
@Api(tags = {"服务下单"})
public class ServiceOrderController extends BaseController {
    @Resource
    private ServiceOrderService serviceOrderService;

    @Resource
    private UserService userService;

    @PostMapping(value = "/create")
    @ApiOperation(value = "创建服务单")
    public BaseResponseInfo create(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            ServiceOrder order = serviceOrderService.createOrder(obj, tenantId, request);
            res.code = 200;
            res.data = order;
        } catch (Exception e) {
            res.code = 500;
            res.data = "创建失败";
        }
        return res;
    }

    @PostMapping(value = "/addItem")
    @ApiOperation(value = "服务单新增项目")
    public BaseResponseInfo addItem(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            ServiceOrderItem item = serviceOrderService.addItem(obj, tenantId, request);
            res.code = 200;
            res.data = item;
        } catch (Exception e) {
            res.code = 500;
            res.data = "新增失败";
        }
        return res;
    }

    @PostMapping(value = "/quickAddItem")
    @ApiOperation(value = "收银台快捷新增项目(自动创建OPEN服务单)")
    public BaseResponseInfo quickAddItem(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            ServiceOrderItem item = serviceOrderService.quickAddItem(obj, tenantId, request);
            res.code = 200;
            res.data = item;
        } catch (Exception e) {
            res.code = 500;
            res.data = "新增失败";
        }
        return res;
    }

    @PutMapping(value = "/item/updateQty")
    @ApiOperation(value = "修改服务单明细数量")
    public BaseResponseInfo updateItemQty(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceOrderService.updateItemQty(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "修改失败";
        }
        return res;
    }

    @PostMapping(value = "/item/delete")
    @ApiOperation(value = "删除服务单明细(软删除)")
    public BaseResponseInfo deleteItem(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceOrderService.deleteItem(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "删除失败";
        }
        return res;
    }

    @GetMapping(value = "/listBySession")
    @ApiOperation(value = "按会话获取服务单")
    public BaseResponseInfo listBySession(@RequestParam("sessionId") Long sessionId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        Map<String, Object> map = new HashMap<>();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            List<ServiceOrder> orders = serviceOrderService.listBySessionId(sessionId, tenantId);
            map.put("rows", orders);
            map.put("total", orders == null ? 0 : orders.size());
            res.code = 200;
            res.data = map;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @GetMapping(value = "/listItems")
    @ApiOperation(value = "获取服务单明细")
    public BaseResponseInfo listItems(@RequestParam("serviceOrderId") Long serviceOrderId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        Map<String, Object> map = new HashMap<>();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            List<ServiceOrderItem> items = serviceOrderService.listItemsByOrderId(serviceOrderId, tenantId);
            map.put("rows", items);
            map.put("total", items == null ? 0 : items.size());
            res.code = 200;
            res.data = map;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @PostMapping(value = "/finish")
    @ApiOperation(value = "完成服务单并固化提成")
    public BaseResponseInfo finish(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceOrderService.finishOrder(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "完成失败";
        }
        return res;
    }

    private Long resolveTenantId(User userInfo) {
        if (userInfo == null) {
            return null;
        }
        if ("admin".equals(userInfo.getLoginName())) {
            return null;
        }
        if (userInfo.getTenantId() == null || userInfo.getTenantId() == 0L) {
            return null;
        }
        return userInfo.getTenantId();
    }
}
