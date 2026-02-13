package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.CashierServiceTimer;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CashierServiceTimerService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Map;

@RestController
@RequestMapping(value = "/cashier/timer")
@Api(tags = {"收银服务倒计时"})
public class CashierServiceTimerController extends BaseController {
    @Resource
    private CashierServiceTimerService cashierServiceTimerService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/current")
    @ApiOperation(value = "获取当前倒计时")
    public BaseResponseInfo current(@RequestParam("sessionId") Long sessionId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Map<String, Object> data = cashierServiceTimerService.currentBySessionId(sessionId, tenantId);
            res.code = 200;
            res.data = data;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage();
        }
        return res;
    }

    @PostMapping(value = "/start")
    @ApiOperation(value = "开始/更新倒计时")
    public BaseResponseInfo start(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            CashierServiceTimer timer = cashierServiceTimerService.start(obj, tenantId, request);
            res.code = 200;
            res.data = timer;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage();
        }
        return res;
    }

    @PostMapping(value = "/finish")
    @ApiOperation(value = "结束倒计时")
    public BaseResponseInfo finish(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            CashierServiceTimer timer = cashierServiceTimerService.finish(obj, tenantId, request);
            res.code = 200;
            res.data = timer;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage();
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
