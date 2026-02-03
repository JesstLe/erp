package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CashierSettlementService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Map;

@RestController
@RequestMapping(value = "/cashier/settlement")
@Api(tags = {"收银结算"})
public class CashierSettlementController extends BaseController {
    @Resource
    private CashierSettlementService cashierSettlementService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/preview")
    @ApiOperation(value = "结算预览")
    public BaseResponseInfo preview(@RequestParam("sessionId") Long sessionId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Map<String, Object> data = cashierSettlementService.preview(sessionId, tenantId);
            res.code = 200;
            res.data = data;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @PostMapping(value = "/checkout")
    @ApiOperation(value = "结算并可选清台")
    public BaseResponseInfo checkout(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Map<String, Object> data = cashierSettlementService.checkout(obj, tenantId, request);
            res.code = 200;
            res.data = data;
        } catch (Exception e) {
            res.code = 500;
            res.data = "结算失败";
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
