package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.CashierSessionProductItem;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CashierCartService;
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
@RequestMapping(value = "/cashier/cart/product")
@Api(tags = {"收银购物车-产品"})
public class CashierCartController extends BaseController {
    @Resource
    private CashierCartService cashierCartService;

    @Resource
    private UserService userService;

    @PostMapping(value = "/add")
    @ApiOperation(value = "添加产品到会话购物车")
    public BaseResponseInfo add(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            CashierSessionProductItem item = cashierCartService.addProduct(obj, tenantId, request);
            res.code = 200;
            res.data = item;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage();
        }
        return res;
    }

    @GetMapping(value = "/listBySession")
    @ApiOperation(value = "按会话获取购物车产品明细")
    public BaseResponseInfo listBySession(@RequestParam("sessionId") Long sessionId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        Map<String, Object> map = new HashMap<>();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            List<CashierSessionProductItem> list = cashierCartService.listProductsBySessionId(sessionId, tenantId);
            map.put("rows", list);
            map.put("total", list == null ? 0 : list.size());
            res.code = 200;
            res.data = map;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @PutMapping(value = "/updateQty")
    @ApiOperation(value = "修改购物车产品数量")
    public BaseResponseInfo updateQty(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = cashierCartService.updateProductQty(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "修改失败";
        }
        return res;
    }

    @PostMapping(value = "/delete")
    @ApiOperation(value = "删除购物车产品(软删除)")
    public BaseResponseInfo delete(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = cashierCartService.deleteProduct(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "删除失败";
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
