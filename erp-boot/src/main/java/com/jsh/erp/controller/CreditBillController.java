package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.CreditBill;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CreditBillService;
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
@RequestMapping(value = "/cashier/credit")
@Api(tags = {"签单挂账"})
public class CreditBillController extends BaseController {
    @Resource
    private CreditBillService creditBillService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/listOpen")
    @ApiOperation(value = "获取未结清挂账列表")
    public BaseResponseInfo listOpen(HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        Map<String, Object> map = new HashMap<>();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            List<CreditBill> list = creditBillService.listByStatus("OPEN", tenantId);
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

    @PostMapping(value = "/create")
    @ApiOperation(value = "创建挂账")
    public BaseResponseInfo create(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = creditBillService.create(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "创建失败";
        }
        return res;
    }

    @PostMapping(value = "/settle")
    @ApiOperation(value = "清账/结清")
    public BaseResponseInfo settle(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = creditBillService.settle(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "清账失败";
        }
        return res;
    }

    @PostMapping(value = "/cancel")
    @ApiOperation(value = "取消挂账")
    public BaseResponseInfo cancel(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = creditBillService.cancel(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "取消失败";
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

