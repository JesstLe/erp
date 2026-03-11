package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.CashierShift;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CashierShiftService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;

@RestController
@RequestMapping(value = "/cashier/shift")
@Api(tags = {"收银交班"})
public class CashierShiftController extends BaseController {
    @Resource
    private CashierShiftService cashierShiftService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/current")
    @ApiOperation(value = "获取当前开班信息")
    public BaseResponseInfo current(@RequestParam("depotId") Long depotId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            CashierShift shift = cashierShiftService.getOpenShift(depotId, userInfo.getId(), tenantId);
            res.code = 200;
            res.data = shift;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @PostMapping(value = "/open")
    @ApiOperation(value = "开班")
    public BaseResponseInfo open(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            CashierShift shift = cashierShiftService.openShift(obj, tenantId, userInfo.getId(), request);
            res.code = 200;
            res.data = shift;
        } catch (Exception e) {
            res.code = 500;
            res.data = "开班失败";
        }
        return res;
    }

    @PostMapping(value = "/handover")
    @ApiOperation(value = "交班")
    public BaseResponseInfo handover(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = cashierShiftService.handover(obj, tenantId, userInfo.getId(), request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "交班失败";
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

